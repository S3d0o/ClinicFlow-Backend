using Domain.Time;

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

        public async Task<BranchCalendar> GetCalendarAsync(int branchId, CancellationToken ct)
        {
            var hours = await context.BranchWorkingHours.AsNoTracking().Where(h => h.BranchId == branchId).ToListAsync(ct);
            var holidays = await context.Holidays.AsNoTracking().Where(h => h.BranchId == null || h.BranchId == branchId).ToListAsync(ct);
            return new BranchCalendar(branchId, hours, holidays);
        }

        public async Task<IReadOnlyDictionary<int, BranchCalendar>> GetCalendarsAsync(CancellationToken ct)
        {
            var branchIds = await context.Branches.Select(b => b.Id).ToListAsync(ct);
            var hours = await context.BranchWorkingHours.AsNoTracking().ToListAsync(ct);
            var holidays = await context.Holidays.AsNoTracking().ToListAsync(ct);
            return branchIds.ToDictionary(id => id, id => new BranchCalendar(id, hours, holidays));
        }

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
