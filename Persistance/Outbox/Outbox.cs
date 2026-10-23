using Domain.Events;
using Domain.Interfaces;

namespace Persistence.Outbox
{
    public sealed class Outbox(ClinicDbContext context) : IOutbox
    {
        public void Add(IDomainEvent domainEvent) => context.OutboxMessages.Add(OutboxSerializer.ToMessage(domainEvent));
    }
}
