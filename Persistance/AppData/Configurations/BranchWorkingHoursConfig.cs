namespace Persistence.AppData.Configurations
{
    internal class BranchWorkingHoursConfig : IEntityTypeConfiguration<BranchWorkingHours>
    {
        public void Configure(EntityTypeBuilder<BranchWorkingHours> builder)
        {
            builder.ToTable("BranchWorkingHours");
            builder.IsAudited("BranchWorkingHours"); // FR-AUD-01: setup edits are last write wins, so the log shows who changed them
            builder.HasIndex(h => new { h.BranchId, h.Weekday, h.From }).IsUnique();
        }
    }
}
