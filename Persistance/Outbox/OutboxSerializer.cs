using System.Text.Json;
using Domain.Entities.Messaging;
using Domain.Events;

namespace Persistence.Outbox
{
    // An event is stored as its full type name and its JSON. Events live in the Domain assembly, so the name is
    // enough to find the type again
    public static class OutboxSerializer
    {
        public static OutboxMessage ToMessage(IDomainEvent domainEvent) => new()
        {
            Type = domainEvent.GetType().FullName!,
            Payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType()),
        };

        public static IDomainEvent FromMessage(string type, string payload)
        {
            var eventType = typeof(IDomainEvent).Assembly.GetType(type)
                ?? throw new InvalidOperationException($"Unknown event type {type}");
            return (IDomainEvent)(JsonSerializer.Deserialize(payload, eventType)
                ?? throw new InvalidOperationException($"Empty payload for {type}"));
        }
    }
}
