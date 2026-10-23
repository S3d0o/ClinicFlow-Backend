namespace Domain.Events
{
    // Something that happened which other parts of the system react to (docs/v2/06 §5). Events are saved as
    // OutboxMessage rows in the same transaction as the change, and delivered by the OutboxDispatcher job.
    // They are serialized to JSON, so keep them small records of ids and values
    public interface IDomainEvent;

    // An entity that records events as its methods change it. The outbox interceptor takes them when the
    // entity is saved
    public interface IHasDomainEvents
    {
        IReadOnlyList<IDomainEvent> TakeDomainEvents();
    }
}
