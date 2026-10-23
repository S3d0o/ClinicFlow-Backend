using Domain.Time;

namespace ClinicFlow.Tests.Domain
{
    // Cairo in 2026: UTC+2 in winter, UTC+3 from Fri 24 Apr 00:00 (the clocks jump to 01:00)
    // until Thu 29 Oct 24:00 (the clocks go back to 23:00, so 23:00–24:00 happens twice)
    public class WallClockTests
    {
        private static readonly TimeZoneInfo Cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");

        private static DateTime? ToUtc(string date, string time) => WallClock.ToUtc(Cairo, DateOnly.Parse(date), TimeOnly.Parse(time));

        private static DateTime Utc(int month, int day, int hour, int minute = 0) => new(2026, month, day, hour, minute, 0, DateTimeKind.Utc);

        [Fact]
        public void ToUtc_InWinter_SubtractsTwoHours()
            => Assert.Equal(Utc(1, 15, 8), ToUtc("2026-01-15", "10:00"));

        [Fact]
        public void ToUtc_InSummer_SubtractsThreeHours()
            => Assert.Equal(Utc(7, 15, 7), ToUtc("2026-07-15", "10:00"));

        [Fact]
        public void ToUtc_ReturnsUtcKind()
            => Assert.Equal(DateTimeKind.Utc, ToUtc("2026-01-15", "10:00")!.Value.Kind);

        [Theory]
        [InlineData("00:00")]
        [InlineData("00:30")]
        [InlineData("00:59")]
        public void ToUtc_TimeSkippedWhenTheClocksGoForward_IsNull(string time)
            => Assert.Null(ToUtc("2026-04-24", time));

        [Fact]
        public void ToUtc_AroundTheSkippedHour_UsesEachSidesOffset()
        {
            Assert.Equal(Utc(4, 23, 21, 59), ToUtc("2026-04-23", "23:59")); // UTC+2
            Assert.Equal(Utc(4, 23, 22), ToUtc("2026-04-24", "01:00"));      // UTC+3
        }

        [Theory]
        [InlineData("23:00", 20, 0)]
        [InlineData("23:30", 20, 30)]
        public void ToUtc_TimeRepeatedWhenTheClocksGoBack_IsItsFirstOccurrence(string time, int utcHour, int utcMinute)
            // Still UTC+3; TimeZoneInfo.GetUtcOffset alone would give the second occurrence (UTC+2), an hour later
            => Assert.Equal(Utc(10, 29, utcHour, utcMinute), ToUtc("2026-10-29", time));

        [Fact]
        public void ToUtc_AfterTheClocksGoBack_SubtractsTwoHours()
            => Assert.Equal(Utc(10, 29, 22), ToUtc("2026-10-30", "00:00"));
    }
}
