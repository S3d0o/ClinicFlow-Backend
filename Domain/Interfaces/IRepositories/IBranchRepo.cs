using Domain.Time;

namespace Domain.Interfaces.IRepositories
{
    public interface IBranchRepo
    {
        // The branch's IANA time zone (Branch.TimeZoneId), for branch-local "today" and slot times (docs/v2/adr/0003)
        Task<string> GetTimeZoneIdAsync(int branchId, CancellationToken ct);
        // Every branch's zone by branch id, for work that spans branches (slot generation)
        Task<IReadOnlyDictionary<int, string>> GetTimeZoneIdsAsync(CancellationToken ct);
        // The branch's opening hours and holidays (FR-ORG-03), for slot generation and schedule checks
        Task<BranchCalendar> GetCalendarAsync(int branchId, CancellationToken ct);
        // Every branch's calendar by branch id, for work that spans branches
        Task<IReadOnlyDictionary<int, BranchCalendar>> GetCalendarsAsync(CancellationToken ct);
        // Tracked, for BranchService's changes
        Task<Branch?> GetByIdAsync(int branchId, CancellationToken ct);
        Task<bool> CodeExistsAsync(string code, int? exceptBranchId, CancellationToken ct);
        Task<int> CountActiveAsync(CancellationToken ct);
        // False for an unknown branch too: a deactivated branch takes no new bookings (FR-ORG-02)
        Task<bool> IsActiveAsync(int branchId, CancellationToken ct);
        void Add(Branch branch);
    }
}
