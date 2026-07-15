namespace Persistence.Repositories
{
    public class PatientRepo(ClinicDbContext context) : IPatientRepo
    {
        public async Task AddAsync(PatientProfile patient, CancellationToken ct)
        => await context.PatientProfiles.AddAsync(patient, ct);

        public void Delete(PatientProfile patient)
        => context.PatientProfiles.Remove(patient);

        public async Task<IEnumerable<PatientProfile>> GetAllPatientsAsync(CancellationToken ct)
        => await context.PatientProfiles.ToListAsync(ct);


        public async Task<PatientProfile?> GetPatientByIdAsync(int id, CancellationToken ct)
        => await context.PatientProfiles.FirstOrDefaultAsync(p => p.Id == id, ct);

        public async Task<PatientProfile?> GetPatientByUserIdAsync(Guid userId, CancellationToken ct)
        => await context.PatientProfiles.Include(p => p.User).FirstOrDefaultAsync(p => p.UserId == userId, ct);

        public void Update(PatientProfile patient)
        => context.PatientProfiles.Update(patient);
    }
}
