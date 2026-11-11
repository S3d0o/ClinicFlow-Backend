namespace Persistence.AppData.Configurations
{
    internal class MessageTemplateConfig : IEntityTypeConfiguration<MessageTemplate>
    {
        public void Configure(EntityTypeBuilder<MessageTemplate> builder)
        {
            builder.ToTable("MessageTemplates");
            builder.IsAudited("MessageTemplate"); // FR-NTF-02: who changed what patients and staff are told
            builder.Property(t => t.Key).HasMaxLength(64);
            builder.Property(t => t.Channel).HasConversion<string>().HasMaxLength(10);
            builder.Property(t => t.Subject).HasMaxLength(200);
            builder.Property(t => t.Body).HasMaxLength(4000);
            builder.Property(t => t.ProviderTemplateName).HasMaxLength(100).IsNotAudited(); // set by the catalog only
            builder.Property(t => t.UpdatedAt).IsNotAudited(); // the entry has its own time
            builder.HasIndex(t => new { t.OrganizationId, t.Key, t.Channel, t.Language }).IsUnique();
        }
    }
}
