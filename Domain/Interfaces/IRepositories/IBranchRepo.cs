namespace Domain.Interfaces.IRepositories
{
    public interface IBranchRepo
    {
        // The branch's IANA time zone (Branch.TimeZoneId), for branch-local "today" and slot times (docs/v2/adr/0003)
        Task<string> GetTimeZoneIdAsync(int branchId, CancellationToken ct);

        // Every branch's zone by branch id, for work that spans branches (slot generation)
        Task<IReadOnlyDictionary<int, string>> GetTimeZoneIdsAsync(CancellationToken ct);
    }
}
