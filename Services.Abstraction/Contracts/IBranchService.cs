using Shared.DTOs.Organization;

namespace Services.Abstraction.Contracts
{
    // Changes to branches and their rooms (FR-ORG-02, 04; API_CONTRACT.md §13.25). Reads are IOrganizationQueries
    public interface IBranchService
    {
        // branches.manage at organization scope
        Task<Result<BranchResponse>> CreateAsync(BranchRequest request, CancellationToken ct);
        // branches.manage covering the branch
        Task<Result<BranchResponse>> UpdateAsync(int branchId, BranchRequest request, CancellationToken ct);
        // branches.manage at organization scope. A deactivated branch takes no new bookings
        Task<Result<BranchResponse>> DeactivateAsync(int branchId, CancellationToken ct);
        Task<Result<BranchResponse>> ActivateAsync(int branchId, CancellationToken ct);
        // rooms.manage covering the branch
        Task<Result<RoomResponse>> AddRoomAsync(int branchId, RoomRequest request, CancellationToken ct);
        Task<Result<RoomResponse>> UpdateRoomAsync(int roomId, RoomRequest request, CancellationToken ct);
    }
}
