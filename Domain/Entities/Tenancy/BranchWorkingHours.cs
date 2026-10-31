namespace Domain.Entities.Tenancy
{
    // One opening period of a branch on a weekday, in the branch's wall clock (FR-ORG-03). A day may have several (a
    // morning and an evening shift); a day with none is closed. Slots are only generated inside these periods
    public class BranchWorkingHours : BaseEntity<int>, IOrganizationOwned, IBranchOwned
    {
        public int OrganizationId { get; set; }
        public int BranchId { get; set; }
        public DayOfWeek Weekday { get; set; }
        public TimeOnly From { get; set; }
        public TimeOnly To { get; set; } // after From: a period never crosses midnight

        // Open around the clock (the API's times are HH:mm, so the day ends at 23:59): what a branch has until its hours
        // are set, so slot generation behaves as it did before branches had hours (migration AddWorkingHoursAndHolidays)
        public static IEnumerable<BranchWorkingHours> AllWeek(int branchId)
            => Enum.GetValues<DayOfWeek>().Select(day => new BranchWorkingHours
            {
                BranchId = branchId, Weekday = day, From = TimeOnly.MinValue, To = new TimeOnly(23, 59)
            });

        // Each period starts before it ends, and no two on the same day overlap; touching ones (09–15, 15–22) are fine
        public static bool AreValid(IEnumerable<(TimeOnly From, TimeOnly To)> dayRanges)
        {
            var sorted = dayRanges.OrderBy(r => r.From).ToList();
            if (sorted.Any(r => r.From >= r.To))
                return false;
            for (var i = 1; i < sorted.Count; i++)
                if (sorted[i].From < sorted[i - 1].To)
                    return false;
            return true;
        }
    }
}
