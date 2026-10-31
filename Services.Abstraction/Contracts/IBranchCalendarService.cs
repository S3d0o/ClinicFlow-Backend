using Shared.DTOs.Organization;

namespace Services.Abstraction.Contracts
{
    // Changes to a branch's opening hours and holidays (FR-ORG-03; API_CONTRACT.md §13.25). Reads are IOrganizationQueries
    public interface IBranchCalendarService
    {
        // branches.manage covering the branch. Replaces the week; a weekday not sent is closed. Existing slots stay
        Task<Result<IReadOnlyList<DayHoursResponse>>> SetWorkingHoursAsync(int branchId, IReadOnlyList<DayHoursRequest> week, CancellationToken ct);
        // branches.manage covering the branch, or held organization-wide for a holiday that closes every branch.
        // Free slots on its days are withdrawn; booked visits stay and are counted
        Task<Result<HolidayAddedResponse>> AddHolidayAsync(HolidayRequest request, CancellationToken ct);
        // As AddHolidayAsync. Slot generation fills the days again on its next run
        Task<Result> RemoveHolidayAsync(int holidayId, CancellationToken ct);
    }
}
