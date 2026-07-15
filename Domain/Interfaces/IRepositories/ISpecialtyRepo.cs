namespace Domain.Interfaces.IRepositories
{
    public interface ISpecialtyRepo
    {
        Task<IReadOnlyCollection<Specialty>> GetAllAsync( CancellationToken ct, bool includeInactive = false);
        Task<Specialty?> GetByIdAsync(int id, CancellationToken ct);
        Task<Specialty?> GetByNameAsync(string name, CancellationToken ct);
        Task<bool> HasAssignedDoctorsAsync(int id, CancellationToken ct);
        Task CreateAsync(Specialty specialty, CancellationToken ct);
        void Delete(Specialty specialty);
    }
}
