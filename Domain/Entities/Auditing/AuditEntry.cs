namespace Domain.Entities.Auditing
{
    // One change to an audited record (FR-AUD-01): who, from where, what changed and in which request. The audit
    // interceptor writes it in the same transaction as the change. It is append-only (FR-AUD-04): nothing updates
    // or deletes it, and saving a changed or deleted entry throws
    public class AuditEntry : IOrganizationOwned
    {
        public long Id { get; init; }
        public int OrganizationId { get; set; }

        public DateTime OccurredAtUtc { get; init; }  // set by the database when the row is inserted

        public Guid? UserId { get; init; }            // null when no user is signed in (registration, background jobs)
        public string? IpAddress { get; init; }

        public AuditAction Action { get; init; }
        public string EntityType { get; init; } = string.Empty; // the audit name of the type, e.g. "Appointment"
        public string EntityId { get; init; } = string.Empty;   // the key; a composite key's values joined by ','

        // JSON object, one member per recorded property: {"old": …, "new": …} (only "new" on Create, only "old" on
        // Delete), or {"changed": true} for a redacted property, whose values are never copied (08 §4)
        public string Changes { get; init; } = "{}";

        public string? Reason { get; init; }          // why, where an action requires one (none does yet)
        public string CorrelationId { get; init; } = string.Empty; // the same for every entry of one request
    }
}
