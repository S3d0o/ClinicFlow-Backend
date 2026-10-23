namespace Domain.Interfaces.IRepositories
{
    // What event handlers check so a retried message isn't sent twice on the same channel (docs/v2/06 §5)
    public interface IMessageDeliveryRepo
    {
        Task<bool> WasDeliveredAsync(long outboxMessageId, string channel, CancellationToken ct);

        // Saved with the next SaveChangesAsync, together with what the channel wrote (e.g. the notifications)
        void Record(long outboxMessageId, string channel, DateTime utcNow);
    }
}
