namespace Shared.DTOs.Organization
{
    // A branch's opening hours and holidays (FR-ORG-03; API_CONTRACT.md §13.23 TimeRange, DayHours, Holiday, HolidayInput)

    // One opening period, branch wall clock; From before To
    public sealed record TimeRangeRequest(TimeOnly From, TimeOnly To);

    // Weekday 0 = Sunday … 6 = Saturday; no ranges: closed
    public sealed record DayHoursRequest(int Weekday, IReadOnlyList<TimeRangeRequest> Ranges);

    public sealed record TimeRangeResponse(TimeOnly From, TimeOnly To);

    public sealed record DayHoursResponse(int Weekday, IReadOnlyList<TimeRangeResponse> Ranges)
    {
        // Seven entries, Sunday first, each day's periods by start time
        public static IReadOnlyList<DayHoursResponse> Week(IEnumerable<(DayOfWeek Weekday, TimeOnly From, TimeOnly To)> periods)
        {
            var byDay = periods.ToLookup(p => p.Weekday);
            return Enumerable.Range(0, 7)
                .Select(day => new DayHoursResponse(day, byDay[(DayOfWeek)day]
                    .OrderBy(p => p.From)
                    .Select(p => new TimeRangeResponse(p.From, p.To))
                    .ToList()))
                .ToList();
        }
    }

    // POST /api/v2/holidays. No BranchId: closes every branch. To is inclusive
    public sealed record HolidayRequest(int? BranchId, string NameEn, string NameAr, DateOnly From, DateOnly To, bool RecursYearly);

    public sealed record HolidayResponse(int Id, int? BranchId, string NameEn, string NameAr, DateOnly From, DateOnly To, bool RecursYearly);

    // AffectedAppointments: the booked visits on the new holiday that need moving (the J5 worklist, P2)
    public sealed record HolidayAddedResponse(HolidayResponse Holiday, int AffectedAppointments);
}
