namespace Persistence.Repositories
{
    public class PatientRepo(ClinicDbContext context) : IPatientRepo
    {
        public async Task AddAsync(PatientProfile patient, CancellationToken ct)
        => await context.PatientProfiles.AddAsync(patient, ct);

        public async Task<PatientProfile?> GetPatientByUserIdAsync(Guid userId, CancellationToken ct)
        => await context.PatientProfiles.Include(p => p.User).FirstOrDefaultAsync(p => p.UserId == userId, ct);

        public async Task<PatientProfile?> GetPatientByUserIdForReadAsync(Guid userId, CancellationToken ct)
        => await context.PatientProfiles.AsNoTracking().Include(p => p.User).FirstOrDefaultAsync(p => p.UserId == userId, ct);
    }
}
