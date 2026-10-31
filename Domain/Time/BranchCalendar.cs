namespace Domain.Time
{
    // A branch's opening hours and holidays (FR-ORG-03), which slot generation and schedule checks ask. Times are the
    // branch's wall clock. Touching periods (09–15 and 15–22) count as one, so a slot may run across the change
    public sealed class BranchCalendar
    {
        private readonly Dictionary<DayOfWeek, List<(TimeOnly From, TimeOnly To)>> _open;
        private readonly List<BranchHoliday> _holidays;

        public BranchCalendar(int branchId, IEnumerable<BranchWorkingHours> hours, IEnumerable<BranchHoliday> holidays)
        {
            _open = hours
                .Where(h => h.BranchId == branchId)
                .GroupBy(h => h.Weekday)
                .ToDictionary(g => g.Key, g => Merge(g.Select(h => (h.From, h.To))));
            _holidays = holidays.Where(h => h.AppliesTo(branchId)).ToList();
        }

        public bool IsHoliday(DateOnly date) => _holidays.Any(h => h.Covers(date));

        // Whether a slot from start to end can be offered on the date: not a holiday, and inside one opening period
        public bool IsOpen(DateOnly date, TimeOnly start, TimeOnly end)
            => !IsHoliday(date) && Periods(date.DayOfWeek).Any(p => p.From <= start && end <= p.To);

        // Whether a doctor's weekly schedule starts and ends while the branch is open on that weekday. The gap between
        // two shifts may fall inside it: generation leaves those times out
        public bool CoversSchedule(DayOfWeek day, TimeOnly start, TimeOnly end)
        {
            var periods = Periods(day);
            return periods.Any(p => p.From <= start && start < p.To) && periods.Any(p => p.From < end && end <= p.To);
        }

        private IReadOnlyList<(TimeOnly From, TimeOnly To)> Periods(DayOfWeek day)
            => _open.TryGetValue(day, out var periods) ? periods : [];

        private static List<(TimeOnly From, TimeOnly To)> Merge(IEnumerable<(TimeOnly From, TimeOnly To)> ranges)
        {
            var merged = new List<(TimeOnly From, TimeOnly To)>();
            foreach (var range in ranges.OrderBy(r => r.From))
            {
                if (merged.Count > 0 && range.From <= merged[^1].To)
                    merged[^1] = (merged[^1].From, range.To > merged[^1].To ? range.To : merged[^1].To);
                else
                    merged.Add(range);
            }
            return merged;
        }
    }
}
