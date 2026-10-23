using Domain.Events;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Persistence.Outbox
{
    // Turns the events of every tracked entity into OutboxMessage rows just before saving, so they are written in
    // the same transaction as the change that raised them (docs/v2/06 §5). The events are taken from the entities,
    // so a later save doesn't write them again. EF's retry strategy re-runs only the database part of a save, not
    // this; a retried ExecuteInTransactionAsync re-runs the operation, which raises the events again
    public sealed class OutboxSaveChangesInterceptor : SaveChangesInterceptor
    {
        public static readonly OutboxSaveChangesInterceptor Instance = new();

        private OutboxSaveChangesInterceptor() { }

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            AddOutboxMessages(eventData.Context);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            AddOutboxMessages(eventData.Context);
            return ValueTask.FromResult(result);
        }

        private static void AddOutboxMessages(DbContext? context)
        {
            if (context is not ClinicDbContext clinic || !clinic.RecordDomainEvents)
                return;

            var events = clinic.ChangeTracker.Entries<IHasDomainEvents>()
                .SelectMany(entry => entry.Entity.TakeDomainEvents())
                .ToList();

            foreach (var domainEvent in events)
                clinic.OutboxMessages.Add(OutboxSerializer.ToMessage(domainEvent));
        }
    }
}
