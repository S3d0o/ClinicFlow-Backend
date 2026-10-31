using Domain.Entities.Tenancy;
using Domain.Time;

namespace ClinicFlow.Tests.Domain
{
    // FR-ORG-03: what slot generation and schedule checks ask a branch's hours and holidays
    public class BranchCalendarTests
    {
        private const int BranchId = 3;
        private static readonly DateOnly Monday = new(2026, 10, 19);

        private static TimeOnly T(string time) => TimeOnly.Parse(time);

        private static BranchWorkingHours Period(DayOfWeek day, string from, string to, int branchId = BranchId)
            => new() { BranchId = branchId, Weekday = day, From = T(from), To = T(to) };

        private static BranchHoliday Holiday(string from, string to, bool yearly = false, int? branchId = BranchId)
            => new() { BranchId = branchId, From = DateOnly.Parse(from), To = DateOnly.Parse(to), RecursYearly = yearly };

        // Monday: a morning and an evening shift with a break; Tuesday: two touching periods; the rest closed
        private static BranchCalendar Calendar(params BranchHoliday[] holidays) => new(BranchId,
            [
                Period(DayOfWeek.Monday, "09:00", "13:00"),
                Period(DayOfWeek.Monday, "16:00", "22:00"),
                Period(DayOfWeek.Tuesday, "09:00", "15:00"),
                Period(DayOfWeek.Tuesday, "15:00", "22:00"),
                Period(DayOfWeek.Wednesday, "00:00", "23:59", branchId: 99), // another branch's
            ], holidays);

        [Theory]
        [InlineData("09:00", "09:30", true)]
        [InlineData("12:30", "13:00", true)]  // ends at closing time
        [InlineData("12:45", "13:15", false)] // runs past closing time
        [InlineData("08:45", "09:15", false)] // starts before opening
        [InlineData("13:00", "13:30", false)] // the break
        [InlineData("21:30", "22:00", true)]
        public void IsOpen_ASlotOnAWorkingDay_OnlyInsideOnePeriod(string start, string end, bool expected)
            => Assert.Equal(expected, Calendar().IsOpen(Monday, T(start), T(end)));

        [Fact]
        public void IsOpen_TouchingPeriods_CountAsOne()
            => Assert.True(Calendar().IsOpen(Monday.AddDays(1), T("14:45"), T("15:15")));

        [Fact]
        public void IsOpen_AClosedDay_IsFalse()
            => Assert.False(Calendar().IsOpen(Monday.AddDays(3), T("10:00"), T("10:30")));

        [Fact]
        public void IsOpen_AnotherBranchesHours_DontCount()
            => Assert.False(Calendar().IsOpen(Monday.AddDays(2), T("10:00"), T("10:30")));

        [Fact]
        public void IsOpen_OnAHoliday_IsFalse()
            => Assert.False(Calendar(Holiday("2026-10-19", "2026-10-19")).IsOpen(Monday, T("09:00"), T("09:30")));

        [Theory]
        [InlineData("09:00", "13:00", true)]
        [InlineData("09:00", "22:00", true)]  // across the break: generation leaves the break out
        [InlineData("10:00", "17:00", true)]
        [InlineData("08:00", "12:00", false)] // starts before opening
        [InlineData("10:00", "22:30", false)] // ends after closing
        [InlineData("13:00", "16:00", false)] // starts at closing time, ends at opening time: nothing open
        [InlineData("14:00", "15:00", false)] // entirely in the break
        public void CoversSchedule_StartAndEndMustFallInOpeningHours(string start, string end, bool expected)
            => Assert.Equal(expected, Calendar().CoversSchedule(DayOfWeek.Monday, T(start), T(end)));

        [Fact]
        public void CoversSchedule_AClosedDay_IsFalse()
            => Assert.False(Calendar().CoversSchedule(DayOfWeek.Friday, T("10:00"), T("11:00")));

        [Theory]
        [InlineData("2026-10-18", false)]
        [InlineData("2026-10-19", true)]
        [InlineData("2026-10-21", true)]
        [InlineData("2026-10-22", false)]
        [InlineData("2027-10-20", false)] // not yearly
        public void IsHoliday_ARange_CoversItsDaysInclusive(string date, bool expected)
            => Assert.Equal(expected, Calendar(Holiday("2026-10-19", "2026-10-21")).IsHoliday(DateOnly.Parse(date)));

        [Theory]
        [InlineData("2026-10-06", true)]
        [InlineData("2031-10-06", true)]
        [InlineData("2020-10-06", true)] // before the first year too: the same days every year
        [InlineData("2031-10-07", false)]
        public void IsHoliday_Yearly_MatchesTheSameDaysEveryYear(string date, bool expected)
            => Assert.Equal(expected, Calendar(Holiday("2026-10-06", "2026-10-06", yearly: true)).IsHoliday(DateOnly.Parse(date)));

        [Theory]
        [InlineData("2027-12-30", true)]
        [InlineData("2028-01-02", true)]
        [InlineData("2028-01-03", false)]
        [InlineData("2027-12-29", false)]
        [InlineData("2027-06-15", false)]
        public void IsHoliday_YearlyAcrossNewYear_Wraps(string date, bool expected)
            => Assert.Equal(expected, Calendar(Holiday("2026-12-30", "2027-01-02", yearly: true)).IsHoliday(DateOnly.Parse(date)));

        [Fact]
        public void IsHoliday_YearlyOnTheLeapDay_OnlyInLeapYears()
        {
            var calendar = Calendar(Holiday("2028-02-29", "2028-02-29", yearly: true));

            Assert.True(calendar.IsHoliday(new DateOnly(2032, 2, 29)));
            Assert.False(calendar.IsHoliday(new DateOnly(2027, 2, 28)));
            Assert.False(calendar.IsHoliday(new DateOnly(2027, 3, 1)));
        }

        [Fact]
        public void IsHoliday_EveryBranchHoliday_Applies_AnotherBranchesDoesNot()
        {
            Assert.True(Calendar(Holiday("2026-10-19", "2026-10-19", branchId: null)).IsHoliday(Monday));
            Assert.False(Calendar(Holiday("2026-10-19", "2026-10-19", branchId: 99)).IsHoliday(Monday));
        }

        [Theory]
        [InlineData(new[] { "09:00-15:00", "15:00-22:00" }, true)] // touching
        [InlineData(new[] { "16:00-22:00", "09:00-13:00" }, true)] // any order
        [InlineData(new string[0], true)]                          // closed
        [InlineData(new[] { "09:00-09:00" }, false)]
        [InlineData(new[] { "15:00-09:00" }, false)]
        [InlineData(new[] { "09:00-15:00", "14:00-22:00" }, false)]
        [InlineData(new[] { "09:00-22:00", "12:00-13:00" }, false)] // one inside another
        public void AreValid_PeriodsMustBeOrderedAndNotOverlap(string[] periods, bool expected)
            => Assert.Equal(expected, BranchWorkingHours.AreValid(periods.Select(p => (T(p[..5]), T(p[6..])))));

        [Fact]
        public void AllWeek_IsOpenAroundTheClockEveryDay()
        {
            var calendar = new BranchCalendar(BranchId, BranchWorkingHours.AllWeek(BranchId), []);

            Assert.All(Enumerable.Range(0, 7), offset => Assert.True(calendar.IsOpen(Monday.AddDays(offset), T("00:00"), T("23:59"))));
        }
    }
}
