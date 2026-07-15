namespace Persistence.AppData.Configurations
{
    internal class NotificationConfig : IEntityTypeConfiguration<Notification>
    {
        public void Configure(EntityTypeBuilder<Notification> builder)
        {
            builder.ToTable("Notifications");
            builder.Property(n => n.Message).IsRequired().HasMaxLength(500);
            builder.Property(n => n.CreatedAt).IsRequired();
            builder.Property(n => n.IsRead).IsRequired();

            // relationship 
            builder.HasOne(n => n.User)
                .WithMany(u => u.Notifications)
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
