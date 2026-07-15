namespace Persistence.AppData.Configurations
{
    internal class DoctorProfileConfig : IEntityTypeConfiguration<DoctorProfile>
    {
        public void Configure(EntityTypeBuilder<DoctorProfile> builder)
        {
            builder.ToTable("Doctors");

            builder.Property(d => d.Bio).HasMaxLength(500);

            builder.Property(d => d.ConsultationFee).HasPrecision(18, 2);

            // Relationships
            builder.HasMany(d=>d.Schedules)
                .WithOne(s=>s.DoctorProfile)
                .HasForeignKey(s=>s.DoctorProfileId)
                .OnDelete(DeleteBehavior.Cascade);

             builder.HasMany(d=>d.Slots)
                .WithOne(s=>s.DoctorProfile)
                .HasForeignKey(s=>s.DoctorProfileId)
                .OnDelete(DeleteBehavior.NoAction);

            builder.HasMany(d=>d.Appointments)
                .WithOne(a=>a.Doctor)
                .HasForeignKey(a=>a.DoctorProfileId)
                .OnDelete(DeleteBehavior.NoAction);

            builder.HasMany(d=>d.Reviews)
                .WithOne(r=>r.Doctor)
                .HasForeignKey(r=>r.DoctorProfileId)
                .OnDelete(DeleteBehavior.NoAction);

            builder.HasOne(d => d.Specialty)
                .WithMany(s=>s.DoctorProfiles)
                .HasForeignKey(d => d.SpecialtyId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(d => d.User)
                .WithOne(u => u.DoctorProfile)
                .HasForeignKey<DoctorProfile>(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade);

        }
    }
}
