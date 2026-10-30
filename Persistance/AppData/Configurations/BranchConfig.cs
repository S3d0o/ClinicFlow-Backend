namespace Persistence.AppData.Configurations
{
    internal class BranchConfig : IEntityTypeConfiguration<Branch>
    {
        public void Configure(EntityTypeBuilder<Branch> builder)
        {
            builder.ToTable("Branches");
            builder.IsAudited("Branch"); // FR-AUD-01: branches scope staff access
            builder.HasIndex(b => new { b.OrganizationId, b.Code }).IsUnique();
            builder.Property(b => b.Code).HasMaxLength(10);
            builder.Property(b => b.NameAr).HasMaxLength(100);
            builder.Property(b => b.NameEn).HasMaxLength(100);
            builder.Property(b => b.AddressEn).HasMaxLength(300);
            builder.Property(b => b.AddressAr).HasMaxLength(300);
            builder.Property(b => b.City).HasMaxLength(100);
            builder.Property(b => b.Phone).HasMaxLength(20);
            builder.Property(b => b.TimeZoneId).HasMaxLength(64);

            // The one branch until P1 adds branch management; see OrganizationConfig
            builder.HasData(new Branch
            {
                Id = Branch.MainId,
                OrganizationId = Organization.DefaultId,
                Code = Branch.MainCode,
                NameAr = "الفرع الرئيسي",
                NameEn = "Main branch",
                TimeZoneId = "Africa/Cairo",
                IsActive = true,
            });
        }
    }
}
