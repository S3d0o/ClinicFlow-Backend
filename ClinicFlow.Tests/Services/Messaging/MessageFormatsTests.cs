using Domain.Enums;
using Services.Messaging;

namespace ClinicFlow.Tests.Services.Messaging
{
    // Dates and times as a message writes them, per language (R10)
    public class MessageFormatsTests
    {
        [Fact]
        public void Date_English_WritesTheDayAndMonthInFull()
            => Assert.Equal("Sunday, November 15, 2026", MessageFormats.Date(new DateOnly(2026, 11, 15), Language.En));

        [Theory]
        [InlineData(2026, 11, 15, "الأحد، 15 نوفمبر 2026")]
        [InlineData(2026, 1, 3, "السبت، 3 يناير 2026")]
        [InlineData(2026, 8, 7, "الجمعة، 7 أغسطس 2026")]
        public void Date_Arabic_WritesTheDayAndMonthInArabic(int year, int month, int day, string expected)
            => Assert.Equal(expected, MessageFormats.Date(new DateOnly(year, month, day), Language.Ar));

        [Theory]
        [InlineData(9, 5, "9:05 AM", "9:05 ص")]
        [InlineData(15, 30, "3:30 PM", "3:30 م")]
        [InlineData(0, 0, "12:00 AM", "12:00 ص")]
        [InlineData(12, 0, "12:00 PM", "12:00 م")]
        public void Time_IsTwelveHour_InBothLanguages(int hour, int minute, string english, string arabic)
        {
            Assert.Equal(english, MessageFormats.Time(new TimeOnly(hour, minute), Language.En));
            Assert.Equal(arabic, MessageFormats.Time(new TimeOnly(hour, minute), Language.Ar));
        }
    }
}
