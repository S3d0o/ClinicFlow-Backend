namespace Domain.Interfaces.IRepositories
{
    public interface IPatientRepo
    {
        // Queries
        // Tracked, for flows that change the profile or use it in a write
        Task<PatientProfile?> GetPatientByUserIdAsync(Guid userId, CancellationToken ct);
        // Untracked, for read-only responses (REVIEW.md NL1)
        Task<PatientProfile?> GetPatientByUserIdForReadAsync(Guid userId, CancellationToken ct);

        // Write
        Task AddAsync(PatientProfile patient, CancellationToken ct);
    }
}
