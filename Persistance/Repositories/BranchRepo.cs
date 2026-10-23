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
    }
}
