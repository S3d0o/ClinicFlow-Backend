using Domain.Events;

namespace Services.Abstraction.Contracts
{
    // Reacts to one kind of event, outside the request that raised it (docs/v2/06 §5). Delivery is at least once,
    // so a handler must be idempotent: check uow.MessageDeliveries for each channel before sending on it.
    // Handlers in the Services assembly are registered automatically (CoreServiceExtensions)
    public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
    {
        Task HandleAsync(TEvent domainEvent, long outboxMessageId, CancellationToken ct);
    }
}
