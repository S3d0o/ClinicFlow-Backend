namespace Persistence.AppData.Configurations
{
    internal class PatientProfileConfig : IEntityTypeConfiguration<PatientProfile>
    {
        public void Configure(EntityTypeBuilder<PatientProfile> builder)
        {
            builder.ToTable("Patients");

            builder.Property(p => p.BloodType).HasConversion<string>();

            // relationships

            builder.HasOne(p => p.User)
                   .WithOne(u => u.PatientProfile)
                   .HasForeignKey<PatientProfile>(p => p.UserId)
                   .OnDelete(DeleteBehavior.Cascade);

            builder.HasMany(p=>p.Reviews)
                .WithOne(r=>r.Patient)
                .HasForeignKey(r=>r.PatientProfileId)
                .OnDelete(DeleteBehavior.NoAction);

            builder.HasMany(p => p.Appointments)
                .WithOne(a => a.Patient)
                .HasForeignKey(a => a.PatientProfileId)
                .OnDelete(DeleteBehavior.NoAction);


        }
    }
}
