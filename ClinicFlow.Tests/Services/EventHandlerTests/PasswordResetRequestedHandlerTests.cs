using Domain.Entities.IdentityModule;
using Domain.Entities.Messaging;
using Domain.Events;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Services.EventHandlers;

namespace ClinicFlow.Tests.Services.EventHandlerTests
{
    public class PasswordResetRequestedHandlerTests : AccountEmailHandlerTestsBase
    {
        private PasswordResetRequestedHandler Sut => new(_userManager, _uow, _emailService, _clock, Substitute.For<ILogger<PasswordResetRequestedHandler>>());

        [Fact]
        public async Task HandleAsync_SendsTheResetEmailWithAnEscapedToken()
        {
            await Sut.HandleAsync(new PasswordResetRequested(_user.Id), MessageId, _ct);

            await _emailService.Received(1).SendPasswordResetAsync("new@test.com", _user.Id, Uri.EscapeDataString(RawToken), _ct);
        }

        [Fact]
        public async Task HandleAsync_WhenSuspendedSinceTheRequest_SendsNothing()
        {
            _user.IsActive = false;

            await Sut.HandleAsync(new PasswordResetRequested(_user.Id), MessageId, _ct);

            await _userManager.DidNotReceive().GeneratePasswordResetTokenAsync(Arg.Any<ApplicationUser>());
            await _emailService.DidNotReceiveWithAnyArgs().SendPasswordResetAsync(default!, default, default!, default);
        }

        [Fact]
        public async Task HandleAsync_WhenTheUserIsGone_SendsNothing()
        {
            _userManager.FindByIdAsync(_user.Id.ToString()).Returns((ApplicationUser?)null);

            await Sut.HandleAsync(new PasswordResetRequested(_user.Id), MessageId, _ct);

            await _emailService.DidNotReceiveWithAnyArgs().SendPasswordResetAsync(default!, default, default!, default);
        }
    }
}
