using Domain.Entities.IdentityModule;
using Domain.Entities.Messaging;
using Domain.Events;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Services.EventHandlers;
using Shared.Messaging;

namespace ClinicFlow.Tests.Services.EventHandlerTests
{
    public class EmailConfirmationRequestedHandlerTests : AccountEmailHandlerTestsBase
    {
        private EmailConfirmationRequestedHandler Sut
            => new(_userManager, _uow, _messenger, _links, _clock, Substitute.For<ILogger<EmailConfirmationRequestedHandler>>());

        [Fact]
        public async Task HandleAsync_SendsTheConfirmationEmailWithAnEscapedTokenInItsLink()
        {
            await Sut.HandleAsync(new EmailConfirmationRequested(_user.Id), MessageId, _ct);

            var email = Assert.Single(_emails);
            Assert.Same(_user, email.To); // unconfirmed, so AcceptsEmail() doesn't apply: this email is how it gets confirmed
            Assert.Equal(TemplateKeys.EmailConfirmation, email.TemplateKey);
            Assert.Equal(new Dictionary<string, string>
            {
                [TemplatePlaceholders.Name] = "Nada Samir",
                [TemplatePlaceholders.ConfirmUrl] = $"{FrontendUrl}/#/confirm-email?userId={_user.Id}&token={Uri.EscapeDataString(RawToken)}",
            }, email.Values);
            _uow.MessageDeliveries.Received(1).Record(MessageId, DeliveryChannels.Email, UtcNow);
        }

        [Fact]
        public async Task HandleAsync_WhenAlreadyConfirmed_SendsNothing()
        {
            _user.EmailConfirmed = true;

            await Sut.HandleAsync(new EmailConfirmationRequested(_user.Id), MessageId, _ct);

            await NoEmailWasSent();
        }

        [Fact]
        public async Task HandleAsync_WhenTheAccountHasNoEmail_SendsNothing()
        {
            _user.Email = null;

            await Sut.HandleAsync(new EmailConfirmationRequested(_user.Id), MessageId, _ct);

            await NoEmailWasSent();
        }

        [Fact]
        public async Task HandleAsync_WhenAlreadySent_CreatesNoTokenAndSendsNothing()
        {
            AlreadyDelivered(DeliveryChannels.Email);

            await Sut.HandleAsync(new EmailConfirmationRequested(_user.Id), MessageId, _ct);

            await _userManager.DidNotReceive().GenerateEmailConfirmationTokenAsync(Arg.Any<ApplicationUser>());
            await NoEmailWasSent();
        }
    }
}
