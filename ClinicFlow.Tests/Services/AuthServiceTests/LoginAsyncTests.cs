using Domain.Entities.IdentityModule;
using NSubstitute;
using Shared.DTOs.Auth;
using Shared.Errors;

namespace ClinicFlow.Tests.Services.AuthServiceTests
{
    public class LoginAsyncTests : AuthServiceTestsBase
    {
        private const string _email = "patient@test.com";
        private const string _password = "Patient@1234";
        private const string _ip = "10.0.0.7";
        private readonly CancellationToken _ct = CancellationToken.None;
        private readonly LoginRequest _request = new(_email, _password);
        private readonly ApplicationUser _user;
        private readonly TokenDto _tokens = new("access", "refresh", new DateTime(2026, 10, 17, 12, 15, 0, DateTimeKind.Utc));

        public LoginAsyncTests()
        {
            _user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                Email = _email,
                FirstName = "Mona",
                LastName = "Adel",
                IsActive = true,
                EmailConfirmed = true
            };

            _userManager.FindByEmailAsync(_email).Returns(_user);
            _userManager.IsLockedOutAsync(_user).Returns(false);
            _userManager.CheckPasswordAsync(_user, _password).Returns(true);
            _userManager.GetRolesAsync(_user).Returns(new List<string> { "Patient" });
            _tokenService.GenerateTokensAsync(_user, Arg.Any<IList<string>>(), _ip, _ct).Returns(_tokens);
        }

        [Fact]
        public async Task LoginAsync_WhenCredentialsValid_ReturnsTokensAndUserDetails()
        {
            var result = await _sut.LoginAsync(_request, _ip, _ct);

            Assert.True(result.IsSuccess);
            Assert.Equal(new LoginResponse("access", "refresh", _tokens.AccessTokenExpiresAt, _email, "Mona Adel", "Patient"), result.Value);
            await _tokenService.Received(1).GenerateTokensAsync(
                _user, Arg.Is<IList<string>>(r => r != null && r.SequenceEqual(new[] { "Patient" })), _ip, _ct);
            await _userManager.Received(1).ResetAccessFailedCountAsync(_user);
        }

        [Fact]
        public async Task LoginAsync_WhenUserNotFound_ReturnsInvalidCredentialsError()
        {
            _userManager.FindByEmailAsync(_email).Returns((ApplicationUser?)null);

            var result = await _sut.LoginAsync(_request, _ip, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AuthErrors.InvalidCredentials, result.Errors);
            await _tokenService.DidNotReceive().GenerateTokensAsync(
                Arg.Any<ApplicationUser>(), Arg.Any<IList<string>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task LoginAsync_WhenLockedOut_ReturnsAccountLockedOutWithoutCheckingPassword()
        {
            _userManager.IsLockedOutAsync(_user).Returns(true);

            var result = await _sut.LoginAsync(_request, _ip, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AuthErrors.AccountLockedOut, result.Errors);
            await _userManager.DidNotReceive().CheckPasswordAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>());
        }

        [Fact]
        public async Task LoginAsync_WhenPasswordWrong_ReturnsInvalidCredentialsAndCountsFailure()
        {
            _userManager.CheckPasswordAsync(_user, _password).Returns(false);

            var result = await _sut.LoginAsync(_request, _ip, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AuthErrors.InvalidCredentials, result.Errors);
            await _userManager.Received(1).AccessFailedAsync(_user);
            await _userManager.DidNotReceive().ResetAccessFailedCountAsync(Arg.Any<ApplicationUser>());
        }

        [Fact]
        public async Task LoginAsync_WhenPasswordWrongForInactiveUnconfirmedUser_ReturnsInvalidCredentials()
        {
            // Account state is only revealed to someone who knows the password
            _user.IsActive = false;
            _user.EmailConfirmed = false;
            _userManager.CheckPasswordAsync(_user, _password).Returns(false);

            var result = await _sut.LoginAsync(_request, _ip, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AuthErrors.InvalidCredentials, result.Errors);
            Assert.DoesNotContain(AuthErrors.AccountNotActive, result.Errors);
            Assert.DoesNotContain(AuthErrors.EmailNotConfirmed, result.Errors);
        }

        [Fact]
        public async Task LoginAsync_WhenAccountInactive_ReturnsAccountNotActiveError()
        {
            _user.IsActive = false;

            var result = await _sut.LoginAsync(_request, _ip, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AuthErrors.AccountNotActive, result.Errors);
            await _tokenService.DidNotReceive().GenerateTokensAsync(
                Arg.Any<ApplicationUser>(), Arg.Any<IList<string>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task LoginAsync_WhenEmailNotConfirmed_ReturnsEmailNotConfirmedError()
        {
            _user.EmailConfirmed = false;

            var result = await _sut.LoginAsync(_request, _ip, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AuthErrors.EmailNotConfirmed, result.Errors);
            await _tokenService.DidNotReceive().GenerateTokensAsync(
                Arg.Any<ApplicationUser>(), Arg.Any<IList<string>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task LoginAsync_WhenUserHasNoRole_ReturnsEmptyRole()
        {
            _userManager.GetRolesAsync(_user).Returns(new List<string>());

            var result = await _sut.LoginAsync(_request, _ip, _ct);

            Assert.True(result.IsSuccess);
            Assert.Equal(string.Empty, result.Value.Role);
        }
    }
}
