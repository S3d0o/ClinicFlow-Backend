namespace Persistence.AppData.Configurations
{
    internal class DoctorScheduleConfig : IEntityTypeConfiguration<DoctorSchedule>
    {
        public void Configure(EntityTypeBuilder<DoctorSchedule> builder)
        {
            builder.ToTable("DoctorSchedules");

            builder.HasMany(s => s.Slots)
             .WithOne(slot => slot.DoctorSchedule)
             .HasForeignKey(slot => slot.DoctorScheduleId)
             .OnDelete(DeleteBehavior.Cascade); // slots die with their schedule — reasonable

        }
    }
}
