namespace Persistence.AppData.Configurations
{
    internal class MessageDeliveryConfig : IEntityTypeConfiguration<MessageDelivery>
    {
        public void Configure(EntityTypeBuilder<MessageDelivery> builder)
        {
            builder.ToTable("MessageDeliveries");
            builder.Property(d => d.Channel).HasMaxLength(50);

            // One delivery per message and channel: the guard against sending twice (docs/v2/06 §5)
            builder.HasIndex(d => new { d.OutboxMessageId, d.Channel }).IsUnique();
        }
    }
}
