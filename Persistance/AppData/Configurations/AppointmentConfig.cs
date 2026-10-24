namespace Persistence.AppData.Configurations
{
    internal class AppointmentConfig : IEntityTypeConfiguration<Appointment>
    {
        public void Configure(EntityTypeBuilder<Appointment> builder)
        {
            builder.ToTable("Appointments");

            // Every booking, transition and note is audited (FR-AUD-01). The visit's reason and the doctor's notes
            // are clinical, so only the fact that they changed is (08 §4); the reminder mark is the job's bookkeeping
            builder.IsAudited("Appointment");
            builder.Property(a => a.ReasonForVisit).IsAuditRedacted();
            builder.Property(a => a.DoctorNotes).IsAuditRedacted();
            builder.Property(a => a.ReminderSentAt).IsNotAudited();

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

            // A reschedule links the new appointment to the one it replaces; each is replaced at most once
            builder.HasOne(a => a.RescheduledFrom)
                .WithOne(a => a.RescheduledTo)
                .HasForeignKey<Appointment>(a => a.RescheduledFromId)
                .OnDelete(DeleteBehavior.NoAction);

            builder.Property(x=> x.Status)
                .HasConversion<string>()
                .HasMaxLength(20);

            builder.Property(x => x.CancelledBy)
                .HasConversion<string>();

        }
    }
}
