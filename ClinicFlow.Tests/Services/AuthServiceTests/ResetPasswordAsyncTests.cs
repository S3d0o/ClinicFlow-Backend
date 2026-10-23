using Domain.Entities.IdentityModule;
using Microsoft.AspNetCore.Identity;
using NSubstitute;
using Shared.DTOs.Auth;
using Shared.Errors;

namespace ClinicFlow.Tests.Services.AuthServiceTests
{
    public class ResetPasswordAsyncTests : AuthServiceTestsBase
    {
        private const string _rawToken = "reset+token/=";
        private const string _newPassword = "NewPass@5678";
        private readonly CancellationToken _ct = new CancellationTokenSource().Token;
        private readonly ApplicationUser _user;
        private readonly ResetPasswordRequest _request;

        public ResetPasswordAsyncTests()
        {
            _user = new ApplicationUser { Id = Guid.NewGuid(), Email = "patient@test.com", IsActive = true };
            _request = new ResetPasswordRequest(_user.Id.ToString(), Uri.EscapeDataString(_rawToken), _newPassword);

            _userManager.FindByIdAsync(_user.Id.ToString()).Returns(_user);
            _userManager.ResetPasswordAsync(_user, _rawToken, _newPassword).Returns(IdentityResult.Success);
            _userManager.UpdateSecurityStampAsync(_user).Returns(IdentityResult.Success);
        }

        [Fact]
        public async Task ResetPasswordAsync_WhenTokenValid_ResetsWithUnescapedToken()
        {
            var result = await _sut.ResetPasswordAsync(_request, _ct);

            Assert.True(result.IsSuccess);
            await _userManager.Received(1).ResetPasswordAsync(_user, _rawToken, _newPassword);
        }

        [Fact]
        public async Task ResetPasswordAsync_WhenTokenValid_RevokesAllSessionsAndRotatesSecurityStamp()
        {
            var result = await _sut.ResetPasswordAsync(_request, _ct);

            Assert.True(result.IsSuccess);
            await _uow.RefreshTokens.Received(1).RevokeAllActiveAsync(_user.Id, "Password reset", null, UtcNow, _ct);
            await _userManager.Received(1).UpdateSecurityStampAsync(_user);
        }

        [Fact]
        public async Task ResetPasswordAsync_WhenUserNotFound_ReturnsUserNotFoundError()
        {
            var unknownId = Guid.NewGuid().ToString();
            _userManager.FindByIdAsync(unknownId).Returns((ApplicationUser?)null);

            var result = await _sut.ResetPasswordAsync(_request with { UserId = unknownId }, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AuthErrors.UserNotFound, result.Errors);
            await _userManager.DidNotReceive().ResetPasswordAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>(), Arg.Any<string>());
        }

        [Fact]
        public async Task ResetPasswordAsync_WhenUserInactive_ReturnsUserNotFoundWithoutResetting()
        {
            _user.IsActive = false;

            var result = await _sut.ResetPasswordAsync(_request, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AuthErrors.UserNotFound, result.Errors);
            await _userManager.DidNotReceive().ResetPasswordAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>(), Arg.Any<string>());
            await _uow.RefreshTokens.DidNotReceive().RevokeAllActiveAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ResetPasswordAsync_WhenIdentityRejectsReset_ReturnsInvalidPasswordResetTokenAndKeepsSessions()
        {
            // Covers an invalid or expired token and a password that fails Identity's policy
            _userManager.ResetPasswordAsync(_user, _rawToken, _newPassword).Returns(Failed("Invalid token."));

            var result = await _sut.ResetPasswordAsync(_request, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AuthErrors.InvalidPasswordResetToken, result.Errors);
            await _uow.RefreshTokens.DidNotReceive().RevokeAllActiveAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
            await _userManager.DidNotReceive().UpdateSecurityStampAsync(Arg.Any<ApplicationUser>());
        }
    }
}
