using Domain.Entities.Tenancy;
using Shared.DTOs.Organization;

namespace Services.Implementations
{
    // Branches and rooms (FR-ORG-02, 04). Branches and rooms are audited, so new rows are inserted in a transaction
    // (their audit entry needs the id the insert gives them)
    public class BranchService(
        IUnitOfWork uow,
        IMapper mapper,
        IAccessGuard accessGuard,
        ILogger<BranchService> logger) : IBranchService
    {
        public async Task<Result<BranchResponse>> CreateAsync(BranchRequest request, CancellationToken ct)
        {
            if (!HoldsOrganizationWide(Permissions.Organization.BranchesManage))
            {
                logger.LogWarning("Branch creation refused: branches.manage isn't held organization-wide");
                return AccessErrors.OutOfScope;
            }
            if (await uow.Branches.CodeExistsAsync(request.Code, null, ct))
            {
                logger.LogWarning("Branch code {Code} is already used", request.Code);
                return BranchErrors.CodeTaken;
            }

            var branch = new Branch { IsActive = true };
            Apply(branch, request);

            return await uow.ExecuteInTransactionAsync<Result<BranchResponse>>(async transaction =>
            {
                uow.Branches.Add(branch);
                await uow.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                logger.LogInformation("Branch {BranchId} created with code {Code}", branch.Id, branch.Code);
                return mapper.Map<BranchResponse>(branch);
            }, ct);
        }

        public async Task<Result<BranchResponse>> UpdateAsync(int branchId, BranchRequest request, CancellationToken ct)
        {
            var branch = await uow.Branches.GetByIdAsync(branchId, ct);
            if (branch is null)
            {
                logger.LogWarning("Branch {BranchId} not found for update", branchId);
                return BranchErrors.NotFound(branchId);
            }
            if (!await accessGuard.CanAccessAsync(Permissions.Organization.BranchesManage, new AccessTarget(BranchId: branchId), ct))
            {
                logger.LogWarning("Branch {BranchId} is outside the user's branches.manage scope", branchId);
                return AccessErrors.OutOfScope;
            }
            if (await uow.Branches.CodeExistsAsync(request.Code, branchId, ct))
            {
                logger.LogWarning("Branch code {Code} is already used", request.Code);
                return BranchErrors.CodeTaken;
            }

            Apply(branch, request);
            await uow.SaveChangesAsync(ct);

            logger.LogInformation("Branch {BranchId} updated", branchId);
            return mapper.Map<BranchResponse>(branch);
        }

        public async Task<Result<BranchResponse>> DeactivateAsync(int branchId, CancellationToken ct)
        {
            var (branch, error) = await GetForActivationAsync(branchId, ct);
            if (error is not null)
                return error;

            if (branch!.IsActive)
            {
                if (await uow.Branches.CountActiveAsync(ct) <= 1)
                {
                    logger.LogWarning("Branch {BranchId} is the only active branch and can't be deactivated", branchId);
                    return BranchErrors.LastActive;
                }
                branch!.IsActive = false;
                await uow.SaveChangesAsync(ct);
                logger.LogInformation("Branch {BranchId} deactivated", branchId);
            }
            return mapper.Map<BranchResponse>(branch!);
        }

        public async Task<Result<BranchResponse>> ActivateAsync(int branchId, CancellationToken ct)
        {
            var (branch, error) = await GetForActivationAsync(branchId, ct);
            if (error is not null)
                return error;

            if (!branch!.IsActive)
            {
                branch!.IsActive = true;
                await uow.SaveChangesAsync(ct);
                logger.LogInformation("Branch {BranchId} activated", branchId);
            }
            return mapper.Map<BranchResponse>(branch!);
        }

        public async Task<Result<RoomResponse>> AddRoomAsync(int branchId, RoomRequest request, CancellationToken ct)
        {
            if (await uow.Branches.GetByIdAsync(branchId, ct) is null)
            {
                logger.LogWarning("Branch {BranchId} not found for a new room", branchId);
                return BranchErrors.NotFound(branchId);
            }
            if (!await accessGuard.CanAccessAsync(Permissions.Organization.RoomsManage, new AccessTarget(BranchId: branchId), ct))
            {
                logger.LogWarning("Branch {BranchId} is outside the user's rooms.manage scope", branchId);
                return AccessErrors.OutOfScope;
            }
            if (await uow.Rooms.NameExistsAsync(branchId, request.NameEn, request.NameAr, null, ct))
            {
                logger.LogWarning("A room named {NameEn} or {NameAr} exists at branch {BranchId}", request.NameEn, request.NameAr, branchId);
                return BranchErrors.RoomNameTaken;
            }

            var room = new Room { BranchId = branchId, NameEn = request.NameEn, NameAr = request.NameAr, Kind = request.Kind, IsActive = true };

            return await uow.ExecuteInTransactionAsync<Result<RoomResponse>>(async transaction =>
            {
                uow.Rooms.Add(room);
                await uow.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                logger.LogInformation("Room {RoomId} added at branch {BranchId}", room.Id, branchId);
                return mapper.Map<RoomResponse>(room);
            }, ct);
        }

        public async Task<Result<RoomResponse>> UpdateRoomAsync(int roomId, RoomRequest request, CancellationToken ct)
        {
            var room = await uow.Rooms.GetByIdAsync(roomId, ct);
            if (room is null)
            {
                logger.LogWarning("Room {RoomId} not found", roomId);
                return BranchErrors.RoomNotFound(roomId);
            }
            if (!await accessGuard.CanAccessAsync(Permissions.Organization.RoomsManage, new AccessTarget(BranchId: room.BranchId), ct))
            {
                // Rooms outside the user's branches don't exist for them (contract: Room.NotFound)
                logger.LogWarning("Room {RoomId} is outside the user's rooms.manage scope", roomId);
                return BranchErrors.RoomNotFound(roomId);
            }
            if (await uow.Rooms.NameExistsAsync(room.BranchId, request.NameEn, request.NameAr, roomId, ct))
            {
                logger.LogWarning("A room named {NameEn} or {NameAr} exists at branch {BranchId}", request.NameEn, request.NameAr, room.BranchId);
                return BranchErrors.RoomNameTaken;
            }

            room.NameEn = request.NameEn;
            room.NameAr = request.NameAr;
            room.Kind = request.Kind;
            room.IsActive = request.IsActive ?? room.IsActive;
            await uow.SaveChangesAsync(ct);

            logger.LogInformation("Room {RoomId} updated", roomId);
            return mapper.Map<RoomResponse>(room);
        }

        // Activating and deactivating change what the whole organization can book, so they need organization scope
        private async Task<(Branch? Branch, Error? Error)> GetForActivationAsync(int branchId, CancellationToken ct)
        {
            var branch = await uow.Branches.GetByIdAsync(branchId, ct);
            if (branch is null)
            {
                logger.LogWarning("Branch {BranchId} not found", branchId);
                return (null, BranchErrors.NotFound(branchId));
            }
            if (!HoldsOrganizationWide(Permissions.Organization.BranchesManage))
            {
                logger.LogWarning("Branch {BranchId} activation refused: branches.manage isn't held organization-wide", branchId);
                return (null, AccessErrors.OutOfScope);
            }
            return (branch, null);
        }

        private bool HoldsOrganizationWide(string permission) => accessGuard.ScopeOf(permission) == PermissionScope.Organization;

        private static void Apply(Branch branch, BranchRequest request)
        {
            branch.Code = request.Code;
            branch.NameEn = request.NameEn;
            branch.NameAr = request.NameAr;
            branch.AddressEn = request.AddressEn;
            branch.AddressAr = request.AddressAr;
            branch.Phone = request.Phone;
            branch.TimeZoneId = request.TimeZone;
        }
    }
}
