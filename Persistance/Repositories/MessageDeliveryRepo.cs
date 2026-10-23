using Domain.Entities.Messaging;

namespace Persistence.Repositories
{
    public class MessageDeliveryRepo(ClinicDbContext context) : IMessageDeliveryRepo
    {
        public Task<bool> WasDeliveredAsync(long outboxMessageId, string channel, CancellationToken ct)
            => context.MessageDeliveries.AnyAsync(d => d.OutboxMessageId == outboxMessageId && d.Channel == channel, ct);

        public void Record(long outboxMessageId, string channel, DateTime utcNow)
            => context.MessageDeliveries.Add(new MessageDelivery { OutboxMessageId = outboxMessageId, Channel = channel, DeliveredAtUtc = utcNow });
    }
}
