namespace Persistence.AppData.Configurations
{
    internal class AppointmentSlotConfig : IEntityTypeConfiguration<AppointmentSlot>
    {
        public void Configure(EntityTypeBuilder<AppointmentSlot> builder)
        {
            // Core search — "available slots for doctor on date"
            builder.HasIndex(s => new { s.DoctorProfileId, s.Date, s.Status });

            // Regeneration lookup — "all slots from this schedule template"
            builder.HasIndex(s => s.DoctorScheduleId);

            builder.Property(s => s.Status).HasConversion<string>();

            // Unique — no duplicate slots
            builder.HasIndex(s => new { s.DoctorProfileId, s.Date, s.StartTime })
                   .IsUnique();

            // Concurrency token
            builder.Property(s => s.RowVersion).IsRowVersion();
        }
    }
}
