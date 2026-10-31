namespace Domain.Interfaces.IRepositories
{
    public interface IWorkingHoursRepo
    {
        // Tracked, so a new week replaces them through the audited save
        Task<List<BranchWorkingHours>> GetByBranchAsync(int branchId, CancellationToken ct);
        // The week of the organization's first branch with hours, which a new branch starts from
        Task<List<BranchWorkingHours>> GetUsualWeekAsync(CancellationToken ct);
        void AddRange(IEnumerable<BranchWorkingHours> hours);
        void RemoveRange(IEnumerable<BranchWorkingHours> hours);
    }
}
