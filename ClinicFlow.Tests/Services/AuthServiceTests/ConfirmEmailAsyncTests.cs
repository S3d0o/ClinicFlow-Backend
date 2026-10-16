using Domain.Entities.IdentityModule;
using Microsoft.AspNetCore.Identity;
using NSubstitute;
using Shared.DTOs.Auth;
using Shared.Errors;

namespace ClinicFlow.Tests.Services.AuthServiceTests
{
    public class ConfirmEmailAsyncTests : AuthServiceTestsBase
    {
        private const string _rawToken = "conf+token/=";
        private readonly CancellationToken _ct = CancellationToken.None;
        private readonly ApplicationUser _user;
        private readonly ConfirmEmailRequest _request;

        public ConfirmEmailAsyncTests()
        {
            _user = new ApplicationUser { Id = Guid.NewGuid(), Email = "patient@test.com" };
            // The link carries the escaped token; the frontend passes it on as it is
            _request = new ConfirmEmailRequest(_user.Id.ToString(), Uri.EscapeDataString(_rawToken));

            _userManager.FindByIdAsync(_user.Id.ToString()).Returns(_user);
            _userManager.ConfirmEmailAsync(_user, _rawToken).Returns(IdentityResult.Success);
        }

        [Fact]
        public async Task ConfirmEmailAsync_WhenTokenValid_ConfirmsWithUnescapedToken()
        {
            var result = await _sut.ConfirmEmailAsync(_request, _ct);

            Assert.True(result.IsSuccess);
            await _userManager.Received(1).ConfirmEmailAsync(_user, _rawToken);
        }

        [Fact]
        public async Task ConfirmEmailAsync_WhenUserNotFound_ReturnsUserNotFoundError()
        {
            var unknownId = Guid.NewGuid().ToString();
            _userManager.FindByIdAsync(unknownId).Returns((ApplicationUser?)null);

            var result = await _sut.ConfirmEmailAsync(_request with { UserId = unknownId }, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AuthErrors.UserNotFound, result.Errors);
            await _userManager.DidNotReceive().ConfirmEmailAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>());
        }

        [Fact]
        public async Task ConfirmEmailAsync_WhenTokenInvalid_ReturnsInvalidEmailConfirmationTokenError()
        {
            _userManager.ConfirmEmailAsync(_user, _rawToken).Returns(Failed("Invalid token."));

            var result = await _sut.ConfirmEmailAsync(_request, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AuthErrors.InvalidEmailConfirmationToken, result.Errors);
        }
    }
}
