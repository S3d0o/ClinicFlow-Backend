using Domain.Entities.IdentityModule;
using NSubstitute;
using Shared.Errors;

namespace ClinicFlow.Tests.Services.TokenServiceTests
{
    public class RefreshTokenAsyncTests : TokenServiceTestsBase
    {
        private const string RawToken = "raw-refresh-token";
        private readonly CancellationToken _ct = new CancellationTokenSource().Token;
        private readonly ApplicationUser _user;
        private readonly RefreshToken _token;

        public RefreshTokenAsyncTests()
        {
            _user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                Email = "mona@test.com",
                FirstName = "Mona",
                LastName = "Hassan",
                IsActive = true,
            };
            _token = new RefreshToken
            {
                Id = 11,
                TokenHash = Hash(RawToken),
                UserId = _user.Id,
                Created = UtcNow.AddDays(-1),
                Expires = UtcNow.AddDays(6),
                CreatedByIp = Ip,
                SecurityStamp = "stamp-1",
            };

            _uow.RefreshTokens.GetRefreshTokenByHashAsync(Hash(RawToken), _ct).Returns(_token);
            _userManager.FindByIdAsync(_user.Id.ToString()).Returns(_user);
            _userManager.GetSecurityStampAsync(_user).Returns("stamp-1");
            _userManager.GetRolesAsync(_user).Returns(["Patient"]);
            _uow.TrySaveChangesAsync(_ct).Returns(true);
        }

        [Fact]
        public async Task RefreshTokenAsync_WhenValid_RotatesTheTokenAndReturnsNewTokens()
        {
            RefreshToken? replacement = null;
            await _uow.RefreshTokens.AddAsync(Arg.Do<RefreshToken>(t => replacement = t), Arg.Any<CancellationToken>());

            var result = await _sut.RefreshTokenAsync(RawToken, Ip, _ct);

            Assert.True(result.IsSuccess);
            Assert.Equal("access-token", result.Value.AccessToken);
            Assert.Equal(AccessTokenExpiresAt, result.Value.ExpiresAt);
            Assert.Equal("mona@test.com", result.Value.Email);
            Assert.Equal("Mona Hassan", result.Value.FullName);
            Assert.Equal("Patient", result.Value.Role);
            Assert.NotEqual(RawToken, result.Value.RefreshToken);

            Assert.NotNull(replacement);
            Assert.Equal(Hash(result.Value.RefreshToken), replacement!.TokenHash);
            Assert.Equal(replacement.TokenHash, _token.ReplacedByTokenHash);
            Assert.Equal(UtcNow, _token.Revoked);
            Assert.Equal(UtcNow, _token.LastUsed);
            Assert.Equal(UtcNow, replacement.Created);
            Assert.Equal("Rotated", _token.RevocationReason);
            Assert.Equal(Ip, _token.LastUsedByIp);
        }

        [Fact]
        public async Task RefreshTokenAsync_WhenTheTokenIsUnknown_ReturnsInvalidRefreshToken()
        {
            var result = await _sut.RefreshTokenAsync("unknown", Ip, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AuthErrors.InvalidRefreshToken, result.Errors);
        }

        [Fact]
        public async Task RefreshTokenAsync_WhenUsedConcurrently_ReturnsInvalidRefreshTokenAndIssuesNoAccessToken()
        {
            // RowVersion: another refresh with the same token saved first (formerly a DbUpdateConcurrencyException catch)
            _uow.TrySaveChangesAsync(_ct).Returns(false);

            var result = await _sut.RefreshTokenAsync(RawToken, Ip, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AuthErrors.InvalidRefreshToken, result.Errors);
            _accessTokenFactory.DidNotReceiveWithAnyArgs().Create(default, default!, default!, default!);
        }

        [Fact]
        public async Task RefreshTokenAsync_WhenTheTokenIsRevoked_ReturnsInvalidRefreshToken()
        {
            _token.Revoked = UtcNow.AddMinutes(-5);

            var result = await _sut.RefreshTokenAsync(RawToken, Ip, _ct);

            Assert.Contains(AuthErrors.InvalidRefreshToken, result.Errors);
            await _uow.DidNotReceive().TrySaveChangesAsync(Arg.Any<CancellationToken>());
        }

        // Expiry is checked against IClock, and a token is already expired at its Expires instant
        [Theory]
        [InlineData(-1)]
        [InlineData(0)]
        public async Task RefreshTokenAsync_WhenTheTokenIsExpired_ReturnsInvalidRefreshToken(int minutesFromNow)
        {
            _token.Expires = UtcNow.AddMinutes(minutesFromNow);

            var result = await _sut.RefreshTokenAsync(RawToken, Ip, _ct);

            Assert.Contains(AuthErrors.InvalidRefreshToken, result.Errors);
        }

        [Fact]
        public async Task RefreshTokenAsync_WhenTheUserIsSuspended_ReturnsInvalidRefreshToken()
        {
            _user.IsActive = false;

            var result = await _sut.RefreshTokenAsync(RawToken, Ip, _ct);

            Assert.Contains(AuthErrors.InvalidRefreshToken, result.Errors);
            await _uow.DidNotReceive().TrySaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task RefreshTokenAsync_WhenTheSecurityStampChanged_ReturnsInvalidRefreshToken()
        {
            // e.g. the password was changed after the token was issued
            _userManager.GetSecurityStampAsync(_user).Returns("stamp-2");

            var result = await _sut.RefreshTokenAsync(RawToken, Ip, _ct);

            Assert.Contains(AuthErrors.InvalidRefreshToken, result.Errors);
        }

        [Fact]
        public async Task RefreshTokenAsync_WhenARotatedTokenIsReusedFromTheSameIpWithinTheWindow_RejectsWithoutRevokingSessions()
        {
            // Two tabs refreshing at once: a race, not a theft
            _token.ReplacedByTokenHash = "next";
            _token.LastUsed = UtcNow.AddSeconds(-10);
            _token.LastUsedByIp = Ip;

            var result = await _sut.RefreshTokenAsync(RawToken, Ip, _ct);

            Assert.Contains(AuthErrors.InvalidRefreshToken, result.Errors);
            await _uow.RefreshTokens.DidNotReceiveWithAnyArgs().RevokeAllActiveAsync(default, default!, default, default, default);
        }

        [Fact]
        public async Task RefreshTokenAsync_WhenARotatedTokenIsReusedFromElsewhere_RevokesEverySessionOfTheUser()
        {
            _token.ReplacedByTokenHash = "next";
            _token.LastUsed = UtcNow.AddSeconds(-10);
            _token.LastUsedByIp = "10.9.9.9";

            var result = await _sut.RefreshTokenAsync(RawToken, Ip, _ct);

            Assert.Contains(AuthErrors.InvalidRefreshToken, result.Errors);
            await _uow.RefreshTokens.Received(1).RevokeAllActiveAsync(_user.Id, Arg.Any<string>(), Ip, UtcNow, _ct);
        }
    }
}
