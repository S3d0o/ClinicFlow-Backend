using Domain.Entities.Subscriptions;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Services.Abstraction.Contracts;
using System.Data.Common;

namespace Persistence.Subscriptions
{
    // Which organizations' cached entitlements a save makes stale (docs/v2/adr/0008 decision 7): a changed
    // subscription or override stales its organization; a changed plan, its modules or its limits stale every one.
    // Collected before the save (afterwards the entries are unchanged) and applied after it, and once more when the
    // transaction it ran in commits, so a reader can't cache the old rows between the save and the commit.
    // ExecuteUpdate/ExecuteDelete skip interceptors: never use them on these tables
    public sealed class EntitlementChanges
    {
        private readonly HashSet<int> _organizations = [];

        public IReadOnlySet<int> Organizations => _organizations;

        public bool AllOrganizations { get; private set; }

        public bool IsEmpty => !AllOrganizations && _organizations.Count == 0;

        public void CollectFrom(ChangeTracker changeTracker)
        {
            foreach (var entry in changeTracker.Entries())
            {
                if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                    continue;

                switch (entry.Entity)
                {
                    case OrganizationSubscription subscription:
                        _organizations.Add(subscription.OrganizationId);
                        break;
                    case OrganizationModuleOverride moduleOverride:
                        _organizations.Add(moduleOverride.OrganizationId);
                        break;
                    case Plan or PlanModule or PlanLimit:
                        AllOrganizations = true;
                        break;
                }
            }
        }

        public void ApplyTo(IEntitlementCache cache)
        {
            if (AllOrganizations)
                cache.InvalidateAll();
            else
                foreach (var organizationId in _organizations)
                    cache.Invalidate(organizationId);
        }

        public void Clear()
        {
            _organizations.Clear();
            AllOrganizations = false;
        }
    }

    // Collects EntitlementChanges on save and invalidates the cache after it. Outside a transaction that is final;
    // inside one, EntitlementTransactionInterceptor invalidates again on commit
    public sealed class EntitlementSaveChangesInterceptor : SaveChangesInterceptor
    {
        public static readonly EntitlementSaveChangesInterceptor Instance = new();

        private EntitlementSaveChangesInterceptor() { }

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            Collect(eventData.Context);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Collect(eventData.Context);
            return ValueTask.FromResult(result);
        }

        public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
        {
            Invalidate(eventData.Context);
            return result;
        }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            Invalidate(eventData.Context);
            return ValueTask.FromResult(result);
        }

        public override void SaveChangesFailed(DbContextErrorEventData eventData) => Forget(eventData.Context);

        public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            Forget(eventData.Context);
            return Task.CompletedTask;
        }

        private static void Collect(DbContext? context)
        {
            if (context is ClinicDbContext { EntitlementCache: not null } clinic)
                clinic.PendingEntitlementChanges.CollectFrom(clinic.ChangeTracker);
        }

        private static void Invalidate(DbContext? context)
        {
            if (context is not ClinicDbContext { EntitlementCache: { } cache } clinic || clinic.PendingEntitlementChanges.IsEmpty)
                return;

            clinic.PendingEntitlementChanges.ApplyTo(cache);
            if (clinic.Database.CurrentTransaction is null)
                clinic.PendingEntitlementChanges.Clear();
        }

        private static void Forget(DbContext? context)
        {
            if (context is ClinicDbContext clinic && clinic.Database.CurrentTransaction is null)
                clinic.PendingEntitlementChanges.Clear();
        }
    }

    // The second invalidation, once the changed rows are visible to other connections
    public sealed class EntitlementTransactionInterceptor : DbTransactionInterceptor
    {
        public static readonly EntitlementTransactionInterceptor Instance = new();

        private EntitlementTransactionInterceptor() { }

        public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
            => Committed(eventData.Context);

        public override Task TransactionCommittedAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            Committed(eventData.Context);
            return Task.CompletedTask;
        }

        public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData)
            => RolledBack(eventData.Context);

        public override Task TransactionRolledBackAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            RolledBack(eventData.Context);
            return Task.CompletedTask;
        }

        private static void Committed(DbContext? context)
        {
            if (context is not ClinicDbContext { EntitlementCache: { } cache } clinic || clinic.PendingEntitlementChanges.IsEmpty)
                return;

            clinic.PendingEntitlementChanges.ApplyTo(cache);
            clinic.PendingEntitlementChanges.Clear();
        }

        // Nothing changed, and the saves inside already invalidated (harmlessly)
        private static void RolledBack(DbContext? context)
        {
            if (context is ClinicDbContext clinic)
                clinic.PendingEntitlementChanges.Clear();
        }
    }
}
