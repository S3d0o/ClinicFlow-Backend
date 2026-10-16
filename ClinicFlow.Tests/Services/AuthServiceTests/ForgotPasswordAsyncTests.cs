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
        public async Task ForgotPasswordAsync_WhenEmailSendFails_Throws()
        {
            // Documented in the contract: 500 if the email cannot be sent. Unlike registration,
            // the email is the whole operation, so the failure is not swallowed
            _emailService.SendPasswordResetAsync(_email, _user.Id, Arg.Any<string>(), _ct)
                .ThrowsAsync(new InvalidOperationException("SMTP unavailable"));

            await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.ForgotPasswordAsync(_request, _ct));
        }
    }
}
