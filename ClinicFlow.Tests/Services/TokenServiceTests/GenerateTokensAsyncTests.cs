using Domain.Entities.IdentityModule;
using NSubstitute;

namespace ClinicFlow.Tests.Services.TokenServiceTests
{
    public class GenerateTokensAsyncTests : TokenServiceTestsBase
    {
        private readonly CancellationToken _ct = new CancellationTokenSource().Token;
        private readonly ApplicationUser _user = new()
        {
            Id = Guid.NewGuid(),
            Email = "mona@test.com",
            FirstName = "Mona",
            LastName = "Hassan",
        };

        public GenerateTokensAsyncTests()
        {
            _userManager.GetSecurityStampAsync(_user).Returns("stamp-1");
        }

        [Fact]
        public async Task GenerateTokensAsync_WhenCalled_IssuesTheAccessTokenFromTheFactory()
        {
            var tokens = await _sut.GenerateTokensAsync(_user, ["Patient"], Ip, _ct);

            Assert.Equal("access-token", tokens.AccessToken);
            Assert.Equal(AccessTokenExpiresAt, tokens.AccessTokenExpiresAt);
            _accessTokenFactory.Received(1).Create(
                _user.Id, "mona@test.com", "Mona Hassan", Arg.Is<IEnumerable<string>>(r => r!.SequenceEqual(new[] { "Patient" })));
        }

        [Fact]
        public async Task GenerateTokensAsync_WhenCalled_StoresOnlyTheHashOfTheRefreshToken()
        {
            RefreshToken? stored = null;
            await _uow.RefreshTokens.AddAsync(Arg.Do<RefreshToken>(t => stored = t), Arg.Any<CancellationToken>());

            var tokens = await _sut.GenerateTokensAsync(_user, ["Patient"], Ip, _ct);

            Assert.NotNull(stored);
            Assert.Equal(Hash(tokens.RefreshToken), stored!.TokenHash);
            Assert.NotEqual(tokens.RefreshToken, stored.TokenHash);
            Assert.Equal(_user.Id, stored.UserId);
            Assert.Equal(Ip, stored.CreatedByIp);
            Assert.Equal("stamp-1", stored.SecurityStamp);
            Assert.Equal(UtcNow, stored.Created); // from IClock
            Assert.Equal(UtcNow.AddDays(_jwt.RefreshTokenExpirationDays), stored.Expires);
            await _uow.Received(1).SaveChangesAsync(_ct);
        }

        [Fact]
        public async Task GenerateTokensAsync_WhenCalledTwice_IssuesDifferentRefreshTokens()
        {
            var first = await _sut.GenerateTokensAsync(_user, [], Ip, _ct);
            var second = await _sut.GenerateTokensAsync(_user, [], Ip, _ct);

            Assert.NotEqual(first.RefreshToken, second.RefreshToken);
        }
    }
}
