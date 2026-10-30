namespace Persistence.AppData.Configurations
{
    internal class OrganizationConfig : IEntityTypeConfiguration<Organization>
    {
        public void Configure(EntityTypeBuilder<Organization> builder)
        {
            builder.ToTable("Organizations");
            builder.IsAudited("Organization"); // FR-AUD-01: the profile printed on invoices
            builder.Property(o => o.LegalName).HasMaxLength(200);
            builder.Property(o => o.BrandNameEn).HasMaxLength(200);
            builder.Property(o => o.BrandNameAr).HasMaxLength(200);
            builder.Property(o => o.Phone).HasMaxLength(20);
            builder.Property(o => o.Email).HasMaxLength(256);
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
                BrandNameEn = "ClinicFlow",
                BrandNameAr = "كلينك فلو",
                DefaultTimeZoneId = "Africa/Cairo",
                IsActive = true,
            });
        }
    }
}
