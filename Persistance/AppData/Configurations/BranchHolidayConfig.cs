namespace Persistence.AppData.Configurations
{
    internal class BranchHolidayConfig : IEntityTypeConfiguration<BranchHoliday>
    {
        public void Configure(EntityTypeBuilder<BranchHoliday> builder)
        {
            builder.ToTable("Holidays");
            builder.IsAudited("Holiday"); // FR-AUD-01
            builder.Property(h => h.NameEn).HasMaxLength(80);
            builder.Property(h => h.NameAr).HasMaxLength(80);

            // Optional, unlike IBranchOwned's: no branch closes them all (ClinicDbContext.ConfigureTenancy adds the organization)
            builder.HasOne<Branch>().WithMany()
                .HasForeignKey(h => h.BranchId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.HasIndex(h => new { h.BranchId, h.From });
        }
    }
}
