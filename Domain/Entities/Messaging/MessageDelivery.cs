namespace Domain.Entities.Messaging
{
    // A channel that has already delivered a message: a retried handler checks it and skips that channel, so a
    // message is never sent twice on the same channel (docs/v2/06 §5)
    public class MessageDelivery : IOrganizationOwned
    {
        public long Id { get; set; }
        public int OrganizationId { get; set; }
        public long OutboxMessageId { get; set; }
        public string Channel { get; set; } = string.Empty; // DeliveryChannels
        public DateTime DeliveredAtUtc { get; set; }

        public OutboxMessage OutboxMessage { get; set; } = null!;
    }

    public static class DeliveryChannels
    {
        public const string InApp = "InApp";
        public const string Email = "Email";
    }
}
