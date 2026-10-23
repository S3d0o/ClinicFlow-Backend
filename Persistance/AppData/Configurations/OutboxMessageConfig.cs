namespace Persistence.AppData.Configurations
{
    internal class OutboxMessageConfig : IEntityTypeConfiguration<OutboxMessage>
    {
        public void Configure(EntityTypeBuilder<OutboxMessage> builder)
        {
            builder.ToTable("OutboxMessages");

            builder.Property(m => m.Type).HasMaxLength(300);
            builder.Property(m => m.LastError).HasMaxLength(2000);

            // The database's clock, so the row needs no time from the code that raised the event. It is only reported:
            // what is due is decided by NextAttemptAtUtc, which the dispatcher sets with the app's clock
            builder.Property(m => m.OccurredAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");

            // What the dispatcher scans for: messages still waiting, in due order (new ones, with no next attempt, first)
            builder.HasIndex(m => new { m.NextAttemptAtUtc, m.Id })
                .HasFilter("[ProcessedAtUtc] IS NULL AND [DeadLetteredAtUtc] IS NULL");

            builder.HasMany(m => m.Deliveries)
                .WithOne(d => d.OutboxMessage)
                .HasForeignKey(d => d.OutboxMessageId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
