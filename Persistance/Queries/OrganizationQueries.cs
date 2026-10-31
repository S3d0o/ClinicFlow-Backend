using Microsoft.Extensions.Logging;
using Services.Abstraction.Contracts;
using Shared.DTOs.Organization;
using Shared.Errors;

namespace Persistence.Queries
{
    // The organization's reads (CQRS-lite), untracked and projected straight to the contract's shapes. The tenancy
    // filters keep branches and rooms to the context's organization; Organization itself is selected by its id
    public class OrganizationQueries(ClinicDbContext context, IClock clock, ILogger<OrganizationQueries> logger) : IOrganizationQueries
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

        public async Task<Result<IReadOnlyList<DayHoursResponse>>> GetWorkingHoursAsync(int branchId, CancellationToken ct)
        {
            if (!await context.Branches.AnyAsync(b => b.Id == branchId, ct))
            {
                logger.LogWarning("Branch {BranchId} not found for its working hours", branchId);
                return BranchErrors.NotFound(branchId);
            }

            var periods = await context.BranchWorkingHours
                .AsNoTracking()
                .Where(h => h.BranchId == branchId)
                .Select(h => new { h.Weekday, h.From, h.To })
                .ToListAsync(ct);
            return Result<IReadOnlyList<DayHoursResponse>>.Ok(DayHoursResponse.Week(periods.Select(p => (p.Weekday, p.From, p.To))));
        }

        public async Task<Result<IReadOnlyList<HolidayResponse>>> ListHolidaysAsync(int branchId, int? year, CancellationToken ct)
        {
            var zone = await context.Branches.Where(b => b.Id == branchId).Select(b => b.TimeZoneId).FirstOrDefaultAsync(ct);
            if (zone is null)
            {
                logger.LogWarning("Branch {BranchId} not found for its holidays", branchId);
                return BranchErrors.NotFound(branchId);
            }

            // A year: the holidays that fall in it. None: this year's (in the branch's calendar) and later. Yearly ones always
            if (year is < 1 or > 9998)
                return Result<IReadOnlyList<HolidayResponse>>.Ok([]);
            var first = new DateOnly(year ?? clock.Today(zone).Year, 1, 1);
            var last = year is null ? DateOnly.MaxValue : first.AddYears(1).AddDays(-1);

            var holidays = await context.Holidays
                .AsNoTracking()
                .Where(h => (h.BranchId == null || h.BranchId == branchId)
                         && (h.RecursYearly || (h.To >= first && h.From <= last)))
                .OrderBy(h => h.From).ThenBy(h => h.Id)
                .Select(h => new HolidayResponse(h.Id, h.BranchId, h.NameEn, h.NameAr, h.From, h.To, h.RecursYearly))
                .ToListAsync(ct);
            return Result<IReadOnlyList<HolidayResponse>>.Ok(holidays);
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
