namespace Domain.Interfaces.IRepositories
{
    public interface IPatientRepo
    {
        // Queries
        Task<PatientProfile?> GetPatientByIdAsync(int id, CancellationToken ct);
        Task<PatientProfile?> GetPatientByUserIdAsync(Guid userId, CancellationToken ct);
        Task<IEnumerable<PatientProfile>> GetAllPatientsAsync(CancellationToken ct);

        // Write
        Task AddAsync(PatientProfile patient, CancellationToken ct);
        void Update(PatientProfile patient);
        void Delete(PatientProfile patient);
    }
}
