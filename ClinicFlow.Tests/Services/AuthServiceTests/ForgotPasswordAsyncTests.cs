using Domain.Entities.IdentityModule;
using Domain.Events;
using NSubstitute;
using Shared.DTOs.Auth;

namespace ClinicFlow.Tests.Services.AuthServiceTests
{
    // The reset email goes through the outbox: the request writes one row, and PasswordResetRequestedHandler creates
    // the token and sends it (its tests cover the token). Every request returns success either way (REVIEW.md NL24)
    public class ForgotPasswordAsyncTests : AuthServiceTestsBase
    {
        private const string _email = "patient@test.com";
        private readonly CancellationToken _ct = new CancellationTokenSource().Token;
        private readonly ForgotPasswordRequest _request = new(_email);
        private readonly ApplicationUser _user;

        public ForgotPasswordAsyncTests()
        {
            _user = new ApplicationUser { Id = Guid.NewGuid(), Email = _email, IsActive = true };
            _userManager.FindByEmailAsync(_email).Returns(_user);
        }

        [Fact]
        public async Task ForgotPasswordAsync_WhenUserActive_QueuesTheResetEmail()
        {
            var result = await _sut.ForgotPasswordAsync(_request, _ct);

            Assert.True(result.IsSuccess);
            Received.InOrder(() =>
            {
                _uow.Outbox.Add(Arg.Is<PasswordResetRequested>(e => e!.UserId == _user.Id));
                _uow.SaveChangesAsync(_ct);
            });
        }

        [Fact]
        public async Task ForgotPasswordAsync_WhenUserActive_CreatesNoTokenInTheRequest()
        {
            // The token is created by the handler when it sends, so none is stored in the outbox
            await _sut.ForgotPasswordAsync(_request, _ct);

            await _userManager.DidNotReceive().GeneratePasswordResetTokenAsync(Arg.Any<ApplicationUser>());
        }

        [Fact]
        public async Task ForgotPasswordAsync_WhenEmailUnknown_ReturnsSuccessWithoutQueuingAnything()
        {
            _userManager.FindByEmailAsync("nobody@test.com").Returns((ApplicationUser?)null);

            var result = await _sut.ForgotPasswordAsync(new ForgotPasswordRequest("nobody@test.com"), _ct);

            Assert.True(result.IsSuccess);
            _uow.Outbox.DidNotReceiveWithAnyArgs().Add(default!);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ForgotPasswordAsync_WhenUserInactive_ReturnsSuccessWithoutQueuingAnything()
        {
            _user.IsActive = false;

            var result = await _sut.ForgotPasswordAsync(_request, _ct);

            Assert.True(result.IsSuccess);
            _uow.Outbox.DidNotReceiveWithAnyArgs().Add(default!);
        }
    }
}
