namespace Persistence.Repositories
{
    public class BranchRepo(ClinicDbContext context) : IBranchRepo
    {
        public async Task<string> GetTimeZoneIdAsync(int branchId, CancellationToken ct)
            => await context.Branches
                .Where(b => b.Id == branchId)
                .Select(b => b.TimeZoneId)
                .SingleAsync(ct);

        public async Task<IReadOnlyDictionary<int, string>> GetTimeZoneIdsAsync(CancellationToken ct)
            => await context.Branches.ToDictionaryAsync(b => b.Id, b => b.TimeZoneId, ct);

        public Task<Branch?> GetByIdAsync(int branchId, CancellationToken ct)
            => context.Branches.FirstOrDefaultAsync(b => b.Id == branchId, ct);

        public Task<bool> CodeExistsAsync(string code, int? exceptBranchId, CancellationToken ct)
            => context.Branches.AnyAsync(b => b.Code == code && b.Id != exceptBranchId, ct);

        public Task<int> CountActiveAsync(CancellationToken ct)
            => context.Branches.CountAsync(b => b.IsActive, ct);

        public Task<bool> IsActiveAsync(int branchId, CancellationToken ct)
            => context.Branches.AnyAsync(b => b.Id == branchId && b.IsActive, ct);

        public void Add(Branch branch) => context.Branches.Add(branch);
    }
}
