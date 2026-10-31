namespace Domain.Interfaces.IRepositories
{
    public interface IHolidayRepo
    {
        // Tracked, for removal
        Task<BranchHoliday?> GetByIdAsync(int holidayId, CancellationToken ct);
        void Add(BranchHoliday holiday);
        void Remove(BranchHoliday holiday);
    }
}
