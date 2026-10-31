namespace Persistence.Repositories
{
    public class HolidayRepo(ClinicDbContext context) : IHolidayRepo
    {
        public Task<BranchHoliday?> GetByIdAsync(int holidayId, CancellationToken ct)
            => context.Holidays.FirstOrDefaultAsync(h => h.Id == holidayId, ct);

        public void Add(BranchHoliday holiday) => context.Holidays.Add(holiday);

        public void Remove(BranchHoliday holiday) => context.Holidays.Remove(holiday);
    }
}
