namespace Persistence.AppData.Configurations
{
    internal class OrganizationConfig : IEntityTypeConfiguration<Organization>
    {
        public void Configure(EntityTypeBuilder<Organization> builder)
        {
            builder.ToTable("Organizations");
            builder.Property(o => o.LegalName).HasMaxLength(200);
            builder.Property(o => o.BrandName).HasMaxLength(200);
            builder.Property(o => o.LogoUrl).HasMaxLength(500);
            builder.Property(o => o.TaxRegistrationNo).HasMaxLength(50);
            builder.Property(o => o.CommercialRegistrationNo).HasMaxLength(50);
            builder.Property(o => o.DefaultTimeZoneId).HasMaxLength(64);

            // V2's one organization. The migration inserts it, so the OrganizationId columns it added could be filled
            // for existing rows; DataSeeder keeps its time zone in line with Clinic:TimeZoneId
            builder.HasData(new Organization
            {
                Id = Organization.DefaultId,
                LegalName = "ClinicFlow",
                BrandName = "ClinicFlow",
                DefaultTimeZoneId = "Africa/Cairo",
                IsActive = true,
            });
        }
    }
}
