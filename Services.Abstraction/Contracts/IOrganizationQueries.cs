using Shared.DTOs.Organization;

namespace Services.Abstraction.Contracts
{
    // The organization's reads (CQRS-lite; API_CONTRACT.md §13.24, §13.25), implemented in Persistence
    public interface IOrganizationQueries
    {
        Task<Result<OrganizationProfileResponse>> GetProfileAsync(CancellationToken ct);
        // Every branch, deactivated ones included, by code
        Task<Result<IReadOnlyList<BranchResponse>>> ListBranchesAsync(CancellationToken ct);
        // Active branches only, for the public site and booking
        Task<Result<IReadOnlyList<BranchResponse>>> ListPublicBranchesAsync(CancellationToken ct);
        // The branch's rooms, inactive included; Branch.NotFound for an unknown branch
        Task<Result<IReadOnlyList<RoomResponse>>> ListRoomsAsync(int branchId, CancellationToken ct);
        // Seven days, Sunday first; Branch.NotFound for an unknown branch
        Task<Result<IReadOnlyList<DayHoursResponse>>> GetWorkingHoursAsync(int branchId, CancellationToken ct);
        // The branch's holidays and those that close every branch, by start date: those in the year, or without one
        // this year's and later (the branch's calendar); yearly ones always
        Task<Result<IReadOnlyList<HolidayResponse>>> ListHolidaysAsync(int branchId, int? year, CancellationToken ct);
    }
}
