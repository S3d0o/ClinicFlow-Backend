using Microsoft.Extensions.Options;
using Services.Messaging;
using Shared.Settings;

namespace ClinicFlow.Tests.Services.Messaging
{
    // The frontend's hash routes for these links are part of the API contract (§8)
    public class FrontendLinksTests
    {
        private static readonly Guid UserId = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

        private static FrontendLinks Create(string frontendBaseUrl) => new(Options.Create(new FrontendSettings { BaseUrl = frontendBaseUrl }));

        [Fact]
        public void ConfirmEmail_UsesTheFrontendHashRoute()
            => Assert.Equal(
                $"https://app.clinicflow.test/#/confirm-email?userId={UserId}&token=abc",
                Create("https://app.clinicflow.test").ConfirmEmail(UserId, "abc"));

        [Fact]
        public void ResetPassword_UsesTheFrontendHashRoute()
            => Assert.Equal(
                $"https://app.clinicflow.test/#/reset-password?userId={UserId}&token=abc",
                Create("https://app.clinicflow.test").ResetPassword(UserId, "abc"));

        [Fact]
        public void Links_TrailingSlashInBaseUrl_IsNotDoubled()
        {
            var links = Create("http://localhost:3000/");

            Assert.StartsWith("http://localhost:3000/#/confirm-email?", links.ConfirmEmail(UserId, "abc"));
            Assert.StartsWith("http://localhost:3000/#/reset-password?", links.ResetPassword(UserId, "abc"));
        }

        [Fact]
        public void Links_AlreadyEscapedToken_IsNotEscapedAgain()
        {
            // The handlers pass Uri.EscapeDataString(token); escaping again would break the token
            var escaped = Uri.EscapeDataString("CfDJ8+a/b==");

            Assert.EndsWith($"&token={escaped}", Create("http://localhost:3000").ConfirmEmail(UserId, escaped));
            Assert.EndsWith($"&token={escaped}", Create("http://localhost:3000").ResetPassword(UserId, escaped));
        }
    }
}
