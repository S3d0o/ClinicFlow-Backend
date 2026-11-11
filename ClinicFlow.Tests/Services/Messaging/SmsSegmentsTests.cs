using Services.Messaging;

namespace ClinicFlow.Tests.Services.Messaging
{
    // How many parts an SMS is sent as: Arabic is UCS-2 (70 characters, then 67 a part), plain Latin GSM-7 (160, then 153)
    public class SmsSegmentsTests
    {
        private static string Arabic(int length) => new('ع', length);
        private static string Latin(int length) => new('a', length);

        [Theory]
        [InlineData(1, 1)]
        [InlineData(70, 1)]
        [InlineData(71, 2)]   // a longer message is split into parts of 67
        [InlineData(134, 2)]
        [InlineData(135, 3)]
        public void Count_ArabicText_CountsUcs2Parts(int length, int expected)
            => Assert.Equal(expected, SmsSegments.Count(Arabic(length)));

        [Theory]
        [InlineData(160, 1)]
        [InlineData(161, 2)]
        [InlineData(306, 2)]
        [InlineData(307, 3)]
        public void Count_PlainLatinText_CountsGsmParts(int length, int expected)
            => Assert.Equal(expected, SmsSegments.Count(Latin(length)));

        [Fact]
        public void Count_OneArabicCharacter_MakesTheWholeTextUcs2()
            => Assert.Equal(2, SmsSegments.Count(Latin(70) + "ع"));

        [Fact]
        public void Count_GsmExtensionCharacters_TakeTwoPlaces()
        {
            Assert.Equal(1, SmsSegments.Count(Latin(158) + "€"));
            Assert.Equal(2, SmsSegments.Count(Latin(159) + "€"));
        }

        [Fact]
        public void Count_EmptyText_IsNoParts()
            => Assert.Equal(0, SmsSegments.Count(""));
    }
}
