namespace Persistence.Repositories
{
    public class WorkingHoursRepo(ClinicDbContext context) : IWorkingHoursRepo
    {
        public Task<List<BranchWorkingHours>> GetByBranchAsync(int branchId, CancellationToken ct)
            => context.BranchWorkingHours.Where(h => h.BranchId == branchId).ToListAsync(ct);

        public async Task<List<BranchWorkingHours>> GetUsualWeekAsync(CancellationToken ct)
        {
            var firstBranchId = await context.BranchWorkingHours
                .OrderBy(h => h.BranchId)
                .Select(h => (int?)h.BranchId)
                .FirstOrDefaultAsync(ct);

            return firstBranchId is null
                ? []
                : await context.BranchWorkingHours.AsNoTracking().Where(h => h.BranchId == firstBranchId).ToListAsync(ct);
        }

        public void AddRange(IEnumerable<BranchWorkingHours> hours) => context.BranchWorkingHours.AddRange(hours);

        public void RemoveRange(IEnumerable<BranchWorkingHours> hours) => context.BranchWorkingHours.RemoveRange(hours);
    }
}
