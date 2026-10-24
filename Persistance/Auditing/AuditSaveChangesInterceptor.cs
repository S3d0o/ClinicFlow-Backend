using Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Persistence.Auditing
{
    // Writes an AuditEntry for every create, update and delete of an audited type (FR-AUD-01), in the same save as the
    // change, so both commit or neither does. Audited types are marked in their configuration (AuditModelBuilderExtensions).
    //
    // A new row whose key the database generates (an identity column) has no id until it is inserted. Its entry is
    // written by a second save right after the first, which is only atomic inside a transaction: such inserts must run
    // in ExecuteInTransactionAsync, and saving one outside a transaction throws.
    //
    // ExecuteUpdate/ExecuteDelete skip interceptors, so they are not audited: never use them on audited properties
    public sealed class AuditSaveChangesInterceptor : SaveChangesInterceptor
    {
        public static readonly AuditSaveChangesInterceptor Instance = new();

        private static readonly JsonSerializerOptions JsonOptions = new() { Converters = { new JsonStringEnumConverter() } };

        private AuditSaveChangesInterceptor() { }

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            Record(eventData.Context);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Record(eventData.Context);
            return ValueTask.FromResult(result);
        }

        public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
        {
            if (AddEntriesOfInserted(eventData.Context) is { } clinic)
                clinic.SaveChanges();
            return result;
        }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (AddEntriesOfInserted(eventData.Context) is { } clinic)
                await clinic.SaveChangesAsync(cancellationToken);
            return result;
        }

        public override void SaveChangesFailed(DbContextErrorEventData eventData)
            => Forget(eventData.Context);

        public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            Forget(eventData.Context);
            return Task.CompletedTask;
        }

        private static void Record(DbContext? context)
        {
            if (context is not ClinicDbContext clinic)
                return;

            var changed = clinic.ChangeTracker.Entries()
                .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .ToList();

            // Checked even while auditing is off: nothing may rewrite history (FR-AUD-04)
            if (changed.FirstOrDefault(e => e.Entity is AuditEntry && e.State != EntityState.Added) is { } edited)
                throw new InvalidOperationException(
                    $"Audit entries are append-only, but entry {((AuditEntry)edited.Entity).Id} is {edited.State}.");

            if (!clinic.RecordAudit)
                return;

            foreach (var entry in changed)
            {
                if (AuditedType.Of(entry.Metadata) is not { } audited)
                    continue;

                if (entry.State == EntityState.Added && entry.Properties.Any(p => p.IsTemporary))
                {
                    if (clinic.Database.CurrentTransaction is null)
                        throw new InvalidOperationException(
                            $"A new {audited.Name} gets its key from the database, so its audit entry is written by a second save. " +
                            "Insert it inside ExecuteInTransactionAsync, so the entry commits with it.");
                    clinic.PendingAuditInserts.Add(entry);
                    continue;
                }

                if (Build(clinic, entry, audited, ActionOf(entry.State)) is { } auditEntry)
                    clinic.AuditEntries.Add(auditEntry);
            }
        }

        // After a save: the entries of the rows it inserted, now that they have their keys. Returns the context when
        // there are entries to save
        private static ClinicDbContext? AddEntriesOfInserted(DbContext? context)
        {
            if (context is not ClinicDbContext clinic || clinic.PendingAuditInserts.Count == 0)
                return null;

            var inserted = clinic.PendingAuditInserts.ToList();
            clinic.PendingAuditInserts.Clear();
            foreach (var entry in inserted)
                clinic.AuditEntries.Add(Build(clinic, entry, AuditedType.Of(entry.Metadata)!, AuditAction.Create)!);
            return clinic;
        }

        private static void Forget(DbContext? context)
        {
            if (context is ClinicDbContext clinic)
                clinic.PendingAuditInserts.Clear();
        }

        private static AuditAction ActionOf(EntityState state) => state switch
        {
            EntityState.Added => AuditAction.Create,
            EntityState.Modified => AuditAction.Update,
            _ => AuditAction.Delete,
        };

        // Null for an update that changed only ignored properties
        private static AuditEntry? Build(ClinicDbContext clinic, EntityEntry entry, AuditedType audited, AuditAction action)
        {
            var changes = new JsonObject();
            foreach (var property in entry.Properties)
            {
                var mode = audited.ModeOf(property.Metadata);
                if (mode == AuditMode.Ignored)
                    continue;

                var old = action == AuditAction.Create ? null : property.OriginalValue;
                var current = action == AuditAction.Delete ? null : property.CurrentValue;
                if (action == AuditAction.Update && (!property.IsModified || Equals(old, current)))
                    continue;
                if (action != AuditAction.Update && (old ?? current) is null)
                    continue; // an empty value on a new or deleted row says nothing

                changes[property.Metadata.Name] = mode == AuditMode.Redacted
                    ? new JsonObject { ["changed"] = true }
                    : Change(action, old, current);
            }

            if (action == AuditAction.Update && changes.Count == 0)
                return null;

            // A role row holds only ids: name the role too, so the entry reads without a lookup. UserManager loads
            // the role it adds or removes, so it is tracked
            if (entry.Entity is IdentityUserRole<Guid> userRole
                && clinic.Roles.Local.FirstOrDefault(r => r.Id == userRole.RoleId)?.Name is { } roleName)
                changes["Role"] = Change(action, roleName, roleName);

            var actor = clinic.AuditActor;
            return new AuditEntry
            {
                UserId = actor.UserId,
                IpAddress = actor.IpAddress,
                Action = action,
                EntityType = audited.Name,
                EntityId = string.Join(',', entry.Metadata.FindPrimaryKey()!.Properties.Select(p => entry.Property(p.Name).CurrentValue)),
                Changes = changes.ToJsonString(),
                CorrelationId = clinic.AuditCorrelationId,
            };
        }

        // {"old": …, "new": …}; a create has no old value and a delete no new one
        private static JsonObject Change(AuditAction action, object? old, object? current)
        {
            var change = new JsonObject();
            if (action != AuditAction.Create)
                change["old"] = JsonSerializer.SerializeToNode(old, JsonOptions);
            if (action != AuditAction.Delete)
                change["new"] = JsonSerializer.SerializeToNode(current, JsonOptions);
            return change;
        }
    }
}
