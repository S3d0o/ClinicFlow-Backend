using Domain.Entities.Tenancy;
using Shared.DTOs.Organization;

namespace Services.Implementations
{
    // A branch's opening hours and holidays (FR-ORG-03). Both are audited, so new rows are inserted in a transaction
    // (their audit entry needs the id the insert gives them)
    public class BranchCalendarService(
        IUnitOfWork uow,
        IAccessGuard accessGuard,
        IClock clock,
        ILogger<BranchCalendarService> logger) : IBranchCalendarService
    {
        // Slots are published at most a few weeks ahead, so a holiday's later days have none to withdraw
        private const int WithdrawalHorizonDays = 366;

        public async Task<Result<IReadOnlyList<DayHoursResponse>>> SetWorkingHoursAsync(
            int branchId, IReadOnlyList<DayHoursRequest> week, CancellationToken ct)
        {
            if (await uow.Branches.GetByIdAsync(branchId, ct) is null)
            {
                logger.LogWarning("Branch {BranchId} not found for its working hours", branchId);
                return BranchErrors.NotFound(branchId);
            }
            if (!await accessGuard.CanAccessAsync(Permissions.Organization.BranchesManage, new AccessTarget(BranchId: branchId), ct))
            {
                logger.LogWarning("Branch {BranchId} is outside the user's branches.manage scope", branchId);
                return AccessErrors.OutOfScope;
            }
            if (!IsValidWeek(week))
            {
                logger.LogWarning("Working hours for branch {BranchId} rejected: a weekday unknown or repeated, or periods out of order or overlapping", branchId);
                return BranchErrors.InvalidHours;
            }

            var wanted = week
                .SelectMany(day => day.Ranges.Select(r => (Weekday: (DayOfWeek)day.Weekday, r.From, r.To)))
                .ToList();

            return await uow.ExecuteInTransactionAsync<Result<IReadOnlyList<DayHoursResponse>>>(async transaction =>
            {
                // Only the periods that changed are removed and added, so the audit log shows the real edit
                var current = await uow.WorkingHours.GetByBranchAsync(branchId, ct);
                uow.WorkingHours.RemoveRange(current.Where(h => !wanted.Contains((h.Weekday, h.From, h.To))).ToList());
                uow.WorkingHours.AddRange(wanted
                    .Where(w => !current.Any(h => (h.Weekday, h.From, h.To) == w))
                    .Select(w => new BranchWorkingHours { BranchId = branchId, Weekday = w.Weekday, From = w.From, To = w.To })
                    .ToList());
                await uow.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                logger.LogInformation("Working hours of branch {BranchId} set: {PeriodCount} periods", branchId, wanted.Count);
                return Result<IReadOnlyList<DayHoursResponse>>.Ok(DayHoursResponse.Week(wanted));
            }, ct);
        }

        public async Task<Result<HolidayAddedResponse>> AddHolidayAsync(HolidayRequest request, CancellationToken ct)
        {
            if (request.BranchId is { } branchId && await uow.Branches.GetByIdAsync(branchId, ct) is null)
            {
                logger.LogWarning("Branch {BranchId} not found for a new holiday", branchId);
                return BranchErrors.NotFound(branchId);
            }
            if (!await CanManageAsync(request.BranchId, ct))
            {
                logger.LogWarning("Holiday for branch {BranchId} refused: outside the user's branches.manage scope", request.BranchId);
                return AccessErrors.OutOfScope;
            }

            var holiday = new BranchHoliday
            {
                BranchId = request.BranchId,
                NameEn = request.NameEn,
                NameAr = request.NameAr,
                From = request.From,
                To = request.To,
                RecursYearly = request.RecursYearly,
            };

            // "Today" differs between branch zones; each branch's visits are counted from its own date
            var todays = (await uow.Branches.GetTimeZoneIdsAsync(ct))
                .Where(z => holiday.BranchId is null || z.Key == holiday.BranchId)
                .ToDictionary(z => z.Key, z => clock.Today(z.Value));
            var earliest = todays.Values.Min();
            var closedDays = Enumerable.Range(0, WithdrawalHorizonDays).Select(earliest.AddDays).Where(holiday.Covers).ToList();

            return await uow.ExecuteInTransactionAsync<Result<HolidayAddedResponse>>(async transaction =>
            {
                uow.Holidays.Add(holiday);
                await uow.SaveChangesAsync(ct);
                var withdrawn = closedDays.Count > 0 ? await uow.Slots.WithdrawFreeSlotsAsync(holiday.BranchId, closedDays, ct) : 0;
                var affected = (await uow.Appointments.GetUpcomingVisitDaysAsync(holiday.BranchId, earliest, ct))
                    .Count(v => todays.TryGetValue(v.BranchId, out var today) && v.Date >= today && holiday.Covers(v.Date));
                await transaction.CommitAsync(ct);

                logger.LogInformation(
                    "Holiday {HolidayId} added for branch {BranchId}: {SlotCount} free slots withdrawn, {AppointmentCount} booked visits affected",
                    holiday.Id, holiday.BranchId, withdrawn, affected);
                return new HolidayAddedResponse(ToResponse(holiday), affected);
            }, ct);
        }

        public async Task<Result> RemoveHolidayAsync(int holidayId, CancellationToken ct)
        {
            var holiday = await uow.Holidays.GetByIdAsync(holidayId, ct);
            if (holiday is null)
            {
                logger.LogWarning("Holiday {HolidayId} not found", holidayId);
                return BranchErrors.HolidayNotFound(holidayId);
            }
            if (!await CanManageAsync(holiday.BranchId, ct))
            {
                logger.LogWarning("Holiday {HolidayId} is outside the user's branches.manage scope", holidayId);
                return AccessErrors.OutOfScope;
            }

            uow.Holidays.Remove(holiday);
            await uow.SaveChangesAsync(ct);

            logger.LogInformation("Holiday {HolidayId} removed", holidayId);
            return Result.Ok();
        }

        // A holiday that closes every branch needs the permission organization-wide (API_CONTRACT.md §13.22 "Scope")
        private async Task<bool> CanManageAsync(int? branchId, CancellationToken ct)
            => branchId is { } id
                ? await accessGuard.CanAccessAsync(Permissions.Organization.BranchesManage, new AccessTarget(BranchId: id), ct)
                : accessGuard.ScopeOf(Permissions.Organization.BranchesManage) == PermissionScope.Organization;

        private static bool IsValidWeek(IReadOnlyList<DayHoursRequest> week)
            => week.All(d => d.Weekday is >= 0 and <= 6 && d.Ranges is not null)
               && week.Select(d => d.Weekday).Distinct().Count() == week.Count
               && week.All(d => BranchWorkingHours.AreValid(d.Ranges.Select(r => (r.From, r.To))));

        private static HolidayResponse ToResponse(BranchHoliday h)
            => new(h.Id, h.BranchId, h.NameEn, h.NameAr, h.From, h.To, h.RecursYearly);
    }
}
