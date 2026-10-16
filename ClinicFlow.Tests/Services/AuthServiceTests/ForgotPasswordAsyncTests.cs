using Domain.Entities.IdentityModule;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shared.DTOs.Auth;

namespace ClinicFlow.Tests.Services.AuthServiceTests
{
    public class ForgotPasswordAsyncTests : AuthServiceTestsBase
    {
        private const string _email = "patient@test.com";
        private const string _rawToken = "reset+token/=";
        private readonly CancellationToken _ct = new CancellationTokenSource().Token;
        private readonly ForgotPasswordRequest _request = new(_email);
        private readonly ApplicationUser _user;

        public ForgotPasswordAsyncTests()
        {
            _user = new ApplicationUser { Id = Guid.NewGuid(), Email = _email, IsActive = true };
            _userManager.FindByEmailAsync(_email).Returns(_user);
            _userManager.GeneratePasswordResetTokenAsync(_user).Returns(_rawToken);
        }

        [Fact]
        public async Task ForgotPasswordAsync_WhenUserActive_SendsResetEmailWithEscapedToken()
        {
            var result = await _sut.ForgotPasswordAsync(_request, _ct);

            Assert.True(result.IsSuccess);
            await _emailService.Received(1).SendPasswordResetAsync(_email, _user.Id, Uri.EscapeDataString(_rawToken), _ct);
        }

        [Fact]
        public async Task ForgotPasswordAsync_WhenEmailUnknown_ReturnsSuccessWithoutSendingEmail()
        {
            _userManager.FindByEmailAsync(_email).Returns((ApplicationUser?)null);

            var result = await _sut.ForgotPasswordAsync(_request, _ct);

            // Same result as a known email, so callers can't discover registered emails
            Assert.True(result.IsSuccess);
            await _userManager.DidNotReceive().GeneratePasswordResetTokenAsync(Arg.Any<ApplicationUser>());
            await _emailService.DidNotReceive().SendPasswordResetAsync(
                Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ForgotPasswordAsync_WhenUserInactive_ReturnsSuccessWithoutSendingEmail()
        {
            _user.IsActive = false;

            var result = await _sut.ForgotPasswordAsync(_request, _ct);

            Assert.True(result.IsSuccess);
            await _userManager.DidNotReceive().GeneratePasswordResetTokenAsync(Arg.Any<ApplicationUser>());
            await _emailService.DidNotReceive().SendPasswordResetAsync(
                Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ForgotPasswordAsync_WhenEmailSendFails_ReturnsSuccess()
        {
            // A failure must look the same as an unknown email, or it reveals which emails exist (REVIEW.md NL24)
            _emailService.SendPasswordResetAsync(_email, _user.Id, Arg.Any<string>(), _ct)
                .ThrowsAsync(new InvalidOperationException("SMTP unavailable"));

            var result = await _sut.ForgotPasswordAsync(_request, _ct);

            Assert.True(result.IsSuccess);
        }

        [Fact]
        public async Task ForgotPasswordAsync_WhenTokenGenerationFails_ReturnsSuccessWithoutSendingEmail()
        {
            _userManager.GeneratePasswordResetTokenAsync(_user).ThrowsAsync(new InvalidOperationException("No token provider"));

            var result = await _sut.ForgotPasswordAsync(_request, _ct);

            Assert.True(result.IsSuccess);
            await _emailService.DidNotReceive().SendPasswordResetAsync(
                Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ForgotPasswordAsync_WhenRequestCancelled_PropagatesCancellation()
        {
            // Only failures are swallowed; a cancelled request still ends as cancelled
            using var cts = new CancellationTokenSource();
            _emailService.SendPasswordResetAsync(_email, _user.Id, Arg.Any<string>(), cts.Token)
                .Returns(_ => { cts.Cancel(); throw new OperationCanceledException(cts.Token); });

            await Assert.ThrowsAsync<OperationCanceledException>(() => _sut.ForgotPasswordAsync(_request, cts.Token));
        }
    }
}
