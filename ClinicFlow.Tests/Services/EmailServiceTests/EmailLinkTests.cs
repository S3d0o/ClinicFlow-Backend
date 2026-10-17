using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Services.Implementations;
using Shared.Settings;

namespace ClinicFlow.Tests.Services.EmailServiceTests
{
    // The frontend's hash routes for these links are part of the API contract (§8); the SMTP send itself isn't tested
    public class EmailLinkTests
    {
        private static readonly Guid UserId = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

        private static EmailService Create(string frontendBaseUrl) => new(
            Options.Create(new EmailSettings()),
            Options.Create(new FrontendSettings { BaseUrl = frontendBaseUrl }),
            NullLogger<EmailService>.Instance);

        [Fact]
        public void ConfirmEmailUrl_UsesTheFrontendHashRoute()
            => Assert.Equal(
                $"https://app.clinicflow.test/#/confirm-email?userId={UserId}&token=abc",
                Create("https://app.clinicflow.test").ConfirmEmailUrl(UserId, "abc"));

        [Fact]
        public void ResetPasswordUrl_UsesTheFrontendHashRoute()
            => Assert.Equal(
                $"https://app.clinicflow.test/#/reset-password?userId={UserId}&token=abc",
                Create("https://app.clinicflow.test").ResetPasswordUrl(UserId, "abc"));

        [Fact]
        public void Urls_TrailingSlashInBaseUrl_IsNotDoubled()
        {
            var service = Create("http://localhost:3000/");

            Assert.StartsWith("http://localhost:3000/#/confirm-email?", service.ConfirmEmailUrl(UserId, "abc"));
            Assert.StartsWith("http://localhost:3000/#/reset-password?", service.ResetPasswordUrl(UserId, "abc"));
        }

        [Fact]
        public void Urls_AlreadyEscapedToken_IsNotEscapedAgain()
        {
            // AuthService passes Uri.EscapeDataString(token); escaping again would break the token
            var escaped = Uri.EscapeDataString("CfDJ8+a/b==");

            Assert.EndsWith($"&token={escaped}", Create("http://localhost:3000").ConfirmEmailUrl(UserId, escaped));
            Assert.EndsWith($"&token={escaped}", Create("http://localhost:3000").ResetPasswordUrl(UserId, escaped));
        }
    }
}
