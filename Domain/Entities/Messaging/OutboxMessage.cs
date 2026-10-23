namespace Domain.Entities.Messaging
{
    // One domain event waiting for delivery (docs/v2/06 §5). Written in the same transaction as the change that
    // raised it; the OutboxDispatcher claims, delivers and marks it. Delivery is at least once
    public class OutboxMessage : IOrganizationOwned
    {
        public long Id { get; set; }
        public int OrganizationId { get; set; }

        public string Type { get; set; } = string.Empty;     // the event's full type name
        public string Payload { get; set; } = string.Empty;  // the event as JSON
        public DateTime OccurredAtUtc { get; set; }          // set by the database when the row is inserted

        public int Attempts { get; set; }
        public DateTime? NextAttemptAtUtc { get; set; }      // backoff after a failure; null (due now) until the first one
        public DateTime? LockedUntilUtc { get; set; }        // claimed by a dispatcher until then
        public DateTime? ProcessedAtUtc { get; set; }        // every handler succeeded
        public DateTime? DeadLetteredAtUtc { get; set; }     // gave up after the last attempt; LastError says why
        public string? LastError { get; set; }

        public ICollection<MessageDelivery> Deliveries { get; set; } = [];
    }
}
