using Domain.Entities.IdentityModule;
using Domain.Enums;

namespace ClinicFlow.Tests.Domain
{
    // A user's language and channel (FR-ACC-06, FR-NTF-04)
    public class ApplicationUserTests
    {
        [Fact]
        public void NewUser_ReadsArabicAndTakesBothChannels()
        {
            var user = new ApplicationUser();

            Assert.Equal((Language.Ar, NotificationChannel.Both), (user.PreferredLanguage, user.NotificationChannel));
        }

        [Theory]
        [InlineData("a@test.com", true, NotificationChannel.Both, true)]
        [InlineData("a@test.com", true, NotificationChannel.Email, true)]
        [InlineData("a@test.com", true, NotificationChannel.Phone, false)]  // chose phone messages only
        [InlineData("a@test.com", false, NotificationChannel.Both, false)]  // never confirmed the address
        [InlineData(null, true, NotificationChannel.Both, false)]           // signed up by phone
        [InlineData("", true, NotificationChannel.Both, false)]
        public void AcceptsEmail_OnlyWithAConfirmedAddressAndAChannelThatIncludesEmail(
            string? email, bool confirmed, NotificationChannel channel, bool expected)
        {
            var user = new ApplicationUser { Email = email, EmailConfirmed = confirmed, NotificationChannel = channel };

            Assert.Equal(expected, user.AcceptsEmail());
        }

        [Theory]
        [InlineData(Language.Ar, "ar")]
        [InlineData(Language.En, "en")]
        public void LanguageCode_IsTheContractsCode(Language language, string code)
        {
            Assert.Equal(code, language.Code());
            Assert.Equal(language, LanguageCodes.Parse(code));
        }

        [Theory]
        [InlineData("EN", Language.En)]
        [InlineData(null, Language.Ar)]
        [InlineData("fr", Language.Ar)]
        public void LanguageParse_AnythingButEnglish_IsArabic(string? code, Language expected)
            => Assert.Equal(expected, LanguageCodes.Parse(code));
    }
}
