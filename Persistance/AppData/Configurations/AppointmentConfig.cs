namespace Persistence.AppData.Configurations
{
    internal class AppointmentConfig : IEntityTypeConfiguration<Appointment>
    {
        public void Configure(EntityTypeBuilder<Appointment> builder)
        {
            builder.ToTable("Appointments");

            // relationships

            builder.HasOne(a=>a.Review)
                .WithOne(r=>r.Appointment)
                .HasForeignKey<Review>(r=>r.AppointmentId)
                .OnDelete(DeleteBehavior.Cascade);

            // one to many to make the rebooking operation possible without deleting the previous appointment and keeping the history of the previous appointment
            builder.HasOne(a => a.Slot)
                 .WithMany(s => s.Appointments) 
                 .HasForeignKey(a => a.SlotId)
                 .OnDelete(DeleteBehavior.NoAction);

            builder.Property(x=> x.Status)
                .HasConversion<string>()
                .HasMaxLength(20);

            builder.Property(x => x.CancelledBy)
                .HasConversion<string>();

        }
    }
}
