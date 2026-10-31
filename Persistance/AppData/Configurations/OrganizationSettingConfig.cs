namespace Persistence.AppData.Configurations
{
    internal class OrganizationSettingConfig : IEntityTypeConfiguration<OrganizationSetting>
    {
        public void Configure(EntityTypeBuilder<OrganizationSetting> builder)
        {
            builder.ToTable("OrganizationSettings");
            builder.IsAudited("OrganizationSetting"); // FR-ORG-05: each changed key with its old and new value
            builder.Property(s => s.Key).HasMaxLength(64);
            builder.Property(s => s.Value).HasMaxLength(400);
            builder.HasIndex(s => new { s.OrganizationId, s.Key }).IsUnique();
        }
    }
}
