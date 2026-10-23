using Domain.Entities.IdentityModule;
using Domain.Entities.Messaging;
using Domain.Events;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Services.EventHandlers;

namespace ClinicFlow.Tests.Services.EventHandlerTests
{
    public class EmailConfirmationRequestedHandlerTests : AccountEmailHandlerTestsBase
    {
        private EmailConfirmationRequestedHandler Sut => new(_userManager, _uow, _emailService, _clock, Substitute.For<ILogger<EmailConfirmationRequestedHandler>>());

        [Fact]
        public async Task HandleAsync_SendsTheConfirmationEmailWithAnEscapedToken()
        {
            await Sut.HandleAsync(new EmailConfirmationRequested(_user.Id), MessageId, _ct);

            await _emailService.Received(1).SendEmailConfirmationAsync("new@test.com", _user.Id, Uri.EscapeDataString(RawToken), _ct);
            _uow.MessageDeliveries.Received(1).Record(MessageId, DeliveryChannels.Email, UtcNow);
        }

        [Fact]
        public async Task HandleAsync_WhenAlreadyConfirmed_SendsNothing()
        {
            _user.EmailConfirmed = true;

            await Sut.HandleAsync(new EmailConfirmationRequested(_user.Id), MessageId, _ct);

            await _emailService.DidNotReceiveWithAnyArgs().SendEmailConfirmationAsync(default!, default, default!, default);
        }

        [Fact]
        public async Task HandleAsync_WhenAlreadySent_CreatesNoTokenAndSendsNothing()
        {
            AlreadyDelivered(DeliveryChannels.Email);

            await Sut.HandleAsync(new EmailConfirmationRequested(_user.Id), MessageId, _ct);

            await _userManager.DidNotReceive().GenerateEmailConfirmationTokenAsync(Arg.Any<ApplicationUser>());
            await _emailService.DidNotReceiveWithAnyArgs().SendEmailConfirmationAsync(default!, default, default!, default);
        }
    }
}
