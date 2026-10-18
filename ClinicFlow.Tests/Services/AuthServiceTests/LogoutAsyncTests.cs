using NSubstitute;
using Shared.DTOs.Auth;
using Shared.Errors;
using Shared.ResultPattern;

namespace ClinicFlow.Tests.Services.AuthServiceTests
{
    public class LogoutAsyncTests : AuthServiceTestsBase
    {
        private const string _ip = "10.0.0.7";
        private readonly CancellationToken _ct = new CancellationTokenSource().Token;
        private readonly Guid _userId = Guid.NewGuid();
        private readonly RefreshTokenRequest _request = new("refresh-token");

        [Fact]
        public async Task LogoutAsync_WhenTokenBelongsToCaller_RevokesItWithLogoutReason()
        {
            _tokenService.RevokeRefreshTokenAsync(_userId, "refresh-token", _ip, "Logout", _ct).Returns(Result.Ok());

            var result = await _sut.LogoutAsync(_userId, _request, _ip, _ct);

            Assert.True(result.IsSuccess);
            await _tokenService.Received(1).RevokeRefreshTokenAsync(_userId, "refresh-token", _ip, "Logout", _ct);
        }

        [Fact]
        public async Task LogoutAsync_WhenAnonymous_RevokesWithoutAnOwnerCheck()
        {
            // REVIEW.md L7: a caller whose access token expired can still log out with the refresh token alone
            _tokenService.RevokeRefreshTokenAsync((Guid?)null, "refresh-token", _ip, "Logout", _ct).Returns(Result.Ok());

            var result = await _sut.LogoutAsync(null, _request, _ip, _ct);

            Assert.True(result.IsSuccess);
            await _tokenService.Received(1).RevokeRefreshTokenAsync((Guid?)null, "refresh-token", _ip, "Logout", _ct);
        }

        [Fact]
        public async Task LogoutAsync_WhenRevokeFails_ReturnsTheSameError()
        {
            _tokenService.RevokeRefreshTokenAsync(_userId, "refresh-token", _ip, "Logout", _ct).Returns(Result.Fail(AuthErrors.InvalidRefreshToken));

            var result = await _sut.LogoutAsync(_userId, _request, _ip, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AuthErrors.InvalidRefreshToken, result.Errors);
        }
    }
}
