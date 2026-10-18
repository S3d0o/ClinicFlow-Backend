using Shared.Settings;

namespace ClinicFlow.Tests.Services
{
    // REVIEW.md NL12: Program.cs warns at startup about the keys MissingSettings reports
    public class EmailSettingsTests
    {
        [Fact]
        public void MissingSettings_WhenOnlyAppsettingsDefaultsAreSet_ReturnsTheSecretKeys()
        {
            // appsettings.json holds the host, port and from-name; the rest comes from secrets
            var settings = new EmailSettings { Host = "smtp.gmail.com", Port = 587, FromName = "ClinicFlow" };

            Assert.Equal(["Username", "Password", "FromAddress"], settings.MissingSettings());
        }

        [Fact]
        public void MissingSettings_WhenFullyConfigured_ReturnsEmpty()
        {
            var settings = new EmailSettings
            {
                Host = "smtp.gmail.com",
                Port = 587,
                Username = "user",
                Password = "app-password",
                FromAddress = "clinic@example.com"
            };

            Assert.Empty(settings.MissingSettings());
        }
    }
}
