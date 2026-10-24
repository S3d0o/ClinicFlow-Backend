namespace Persistence.AppData.Configurations
{
    internal class AuditEntryConfig : IEntityTypeConfiguration<AuditEntry>
    {
        public void Configure(EntityTypeBuilder<AuditEntry> builder)
        {
            builder.ToTable("AuditEntries");

            builder.Property(a => a.Action).HasConversion<string>().HasMaxLength(20);
            builder.Property(a => a.EntityType).HasMaxLength(100);
            builder.Property(a => a.EntityId).HasMaxLength(100);
            builder.Property(a => a.IpAddress).HasMaxLength(45); // the longest IPv6 text form
            builder.Property(a => a.Reason).HasMaxLength(1000);
            builder.Property(a => a.CorrelationId).HasMaxLength(64);

            // The database's clock, as for outbox messages: the interceptor needs no time from the code being audited
            builder.Property(a => a.OccurredAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");

            // No foreign key to the user: an entry outlives the account it names. The indexes serve the P1 audit
            // viewer's filters (FR-AUD-03): one record's history, and one user's actions
            builder.HasIndex(a => new { a.EntityType, a.EntityId });
            builder.HasIndex(a => a.UserId);
        }
    }
}
