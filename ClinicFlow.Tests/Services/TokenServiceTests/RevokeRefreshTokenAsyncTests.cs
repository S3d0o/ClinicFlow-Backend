using Domain.Entities.IdentityModule;
using NSubstitute;
using Shared.Errors;

namespace ClinicFlow.Tests.Services.TokenServiceTests
{
    public class RevokeRefreshTokenAsyncTests : TokenServiceTestsBase
    {
        private const string RawToken = "raw-refresh-token";
        private readonly CancellationToken _ct = new CancellationTokenSource().Token;
        private readonly Guid _ownerId = Guid.NewGuid();
        private readonly RefreshToken _token;

        public RevokeRefreshTokenAsyncTests()
        {
            _token = new RefreshToken { Id = 5, TokenHash = Hash(RawToken), UserId = _ownerId, Expires = DateTime.UtcNow.AddDays(1) };
            _uow.RefreshTokens.GetRefreshTokenByHashAsync(Hash(RawToken), _ct).Returns(_token);
        }

        [Fact]
        public async Task RevokeRefreshTokenAsync_WhenTheOwnerRevokes_RevokesAndSaves()
        {
            var result = await _sut.RevokeRefreshTokenAsync(_ownerId, RawToken, Ip, "Logout", _ct);

            Assert.True(result.IsSuccess);
            Assert.NotNull(_token.Revoked);
            Assert.Equal(Ip, _token.RevokedByIp);
            Assert.Equal("Logout", _token.RevocationReason);
            await _uow.Received(1).SaveChangesAsync(_ct);
        }

        [Fact]
        public async Task RevokeRefreshTokenAsync_WhenAnonymousCallerHoldsTheToken_RevokesIt()
        {
            // An anonymous logout proves ownership by holding the token (REVIEW.md L7)
            var result = await _sut.RevokeRefreshTokenAsync(null, RawToken, Ip, null, _ct);

            Assert.True(result.IsSuccess);
            Assert.Equal("RevokedByUser", _token.RevocationReason);
        }

        [Fact]
        public async Task RevokeRefreshTokenAsync_WhenTheTokenBelongsToAnotherUser_ReturnsInvalidRefreshToken()
        {
            var result = await _sut.RevokeRefreshTokenAsync(Guid.NewGuid(), RawToken, Ip, null, _ct);

            Assert.Contains(AuthErrors.InvalidRefreshToken, result.Errors);
            Assert.Null(_token.Revoked);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task RevokeRefreshTokenAsync_WhenTheTokenIsUnknown_ReturnsInvalidRefreshToken()
        {
            var result = await _sut.RevokeRefreshTokenAsync(_ownerId, "unknown", Ip, null, _ct);

            Assert.Contains(AuthErrors.InvalidRefreshToken, result.Errors);
        }

        [Fact]
        public async Task RevokeRefreshTokenAsync_WhenAlreadyRevoked_SucceedsWithoutChangingIt()
        {
            var revokedAt = DateTime.UtcNow.AddHours(-1);
            _token.Revoked = revokedAt;
            _token.RevocationReason = "Rotated";

            var result = await _sut.RevokeRefreshTokenAsync(_ownerId, RawToken, Ip, "Logout", _ct);

            Assert.True(result.IsSuccess);
            Assert.Equal(revokedAt, _token.Revoked);
            Assert.Equal("Rotated", _token.RevocationReason);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }
}
