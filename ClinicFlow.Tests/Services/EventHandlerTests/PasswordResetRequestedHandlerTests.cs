using Domain.Entities.IdentityModule;
using Domain.Events;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Services.EventHandlers;
using Shared.Messaging;

namespace ClinicFlow.Tests.Services.EventHandlerTests
{
    public class PasswordResetRequestedHandlerTests : AccountEmailHandlerTestsBase
    {
        private PasswordResetRequestedHandler Sut
            => new(_userManager, _uow, _messenger, _links, _clock, Substitute.For<ILogger<PasswordResetRequestedHandler>>());

        [Fact]
        public async Task HandleAsync_SendsTheResetEmailWithAnEscapedTokenInItsLink()
        {
            await Sut.HandleAsync(new PasswordResetRequested(_user.Id), MessageId, _ct);

            var email = Assert.Single(_emails);
            Assert.Same(_user, email.To);
            Assert.Equal(TemplateKeys.PasswordReset, email.TemplateKey);
            Assert.Equal(new Dictionary<string, string>
            {
                [TemplatePlaceholders.Name] = "Nada Samir",
                [TemplatePlaceholders.ResetUrl] = $"{FrontendUrl}/#/reset-password?userId={_user.Id}&token={Uri.EscapeDataString(RawToken)}",
            }, email.Values);
        }

        [Fact]
        public async Task HandleAsync_WhenSuspendedSinceTheRequest_SendsNothing()
        {
            _user.IsActive = false;

            await Sut.HandleAsync(new PasswordResetRequested(_user.Id), MessageId, _ct);

            await _userManager.DidNotReceive().GeneratePasswordResetTokenAsync(Arg.Any<ApplicationUser>());
            await NoEmailWasSent();
        }

        [Fact]
        public async Task HandleAsync_WhenTheUserIsGone_SendsNothing()
        {
            _userManager.FindByIdAsync(_user.Id.ToString()).Returns((ApplicationUser?)null);

            await Sut.HandleAsync(new PasswordResetRequested(_user.Id), MessageId, _ct);

            await NoEmailWasSent();
        }
    }
}
