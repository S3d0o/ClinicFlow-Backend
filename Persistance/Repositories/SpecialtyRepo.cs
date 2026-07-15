namespace Persistence.Repositories;

public class SpecialtyRepo(ClinicDbContext context) : ISpecialtyRepo
{
    public async Task CreateAsync(Specialty specialty, CancellationToken ct)
        => await context.Specialties.AddAsync(specialty, ct);

    public void Delete(Specialty specialty)
        => context.Specialties.Remove(specialty);

    public async Task<IReadOnlyCollection<Specialty>> GetAllAsync(
         CancellationToken ct = default, bool includeInactive = false)
        => await context.Specialties
            .Where(s => includeInactive || s.IsActive)
            .ToListAsync(ct);

    public async Task<Specialty?> GetByIdAsync(int id, CancellationToken ct)
        => await context.Specialties
            .FirstOrDefaultAsync(s => s.Id == id && s.IsActive, ct);

    public async Task<Specialty?> GetByNameAsync(string name, CancellationToken ct)
        => await context.Specialties
            .FirstOrDefaultAsync(s => s.Name == name, ct);

    public async Task<bool> HasAssignedDoctorsAsync(int id, CancellationToken ct)
        => await context.Specialties
            .AnyAsync(s => s.Id == id && s.DoctorProfiles.Any(), ct);

    
}