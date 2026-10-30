using Microsoft.Extensions.Logging;
using Services.Abstraction.Contracts;
using Shared.DTOs.Organization;
using Shared.Errors;

namespace Persistence.Queries
{
    // The organization's reads (CQRS-lite), untracked and projected straight to the contract's shapes. The tenancy
    // filters keep branches and rooms to the context's organization; Organization itself is selected by its id
    public class OrganizationQueries(ClinicDbContext context, ILogger<OrganizationQueries> logger) : IOrganizationQueries
    {
        public async Task<Result<OrganizationProfileResponse>> GetProfileAsync(CancellationToken ct)
            => await context.Organizations
                .AsNoTracking()
                .Where(o => o.Id == context.OrganizationId)
                .Select(o => new OrganizationProfileResponse(
                    o.LegalName, o.BrandNameEn, o.BrandNameAr,
                    o.TaxRegistrationNo ?? "", o.CommercialRegistrationNo ?? "", o.Phone ?? "", o.Email ?? ""))
                .SingleAsync(ct);

        public async Task<Result<IReadOnlyList<BranchResponse>>> ListBranchesAsync(CancellationToken ct)
            => Result<IReadOnlyList<BranchResponse>>.Ok(await ListBranchesAsync(activeOnly: false, ct));

        public async Task<Result<IReadOnlyList<BranchResponse>>> ListPublicBranchesAsync(CancellationToken ct)
            => Result<IReadOnlyList<BranchResponse>>.Ok(await ListBranchesAsync(activeOnly: true, ct));

        public async Task<Result<IReadOnlyList<RoomResponse>>> ListRoomsAsync(int branchId, CancellationToken ct)
        {
            if (!await context.Branches.AnyAsync(b => b.Id == branchId, ct))
            {
                logger.LogWarning("Branch {BranchId} not found for its rooms", branchId);
                return BranchErrors.NotFound(branchId);
            }

            var rooms = await context.Rooms
                .AsNoTracking()
                .Where(r => r.BranchId == branchId)
                .OrderBy(r => r.NameEn)
                .Select(r => new RoomResponse(r.Id, r.BranchId, r.NameEn, r.NameAr, r.Kind, r.IsActive))
                .ToListAsync(ct);
            return Result<IReadOnlyList<RoomResponse>>.Ok(rooms);
        }

        private async Task<IReadOnlyList<BranchResponse>> ListBranchesAsync(bool activeOnly, CancellationToken ct)
            => await context.Branches
                .AsNoTracking()
                .Where(b => !activeOnly || b.IsActive)
                .OrderBy(b => b.Code)
                .Select(b => new BranchResponse(
                    b.Id, b.Code, b.NameEn, b.NameAr, b.AddressEn ?? "", b.AddressAr ?? "", b.Phone ?? "", b.TimeZoneId, b.IsActive))
                .ToListAsync(ct);
    }
}
