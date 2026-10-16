using NSubstitute;
using Shared.DTOs.Auth;
using Shared.Errors;
using Shared.ResultPattern;

namespace ClinicFlow.Tests.Services.AuthServiceTests
{
    public class RefreshTokenAsyncTests : AuthServiceTestsBase
    {
        private const string _ip = "10.0.0.7";
        private readonly CancellationToken _ct = new CancellationTokenSource().Token;
        private readonly RefreshTokenRequest _request = new("refresh-token");

        [Fact]
        public async Task RefreshTokenAsync_WhenTokenValid_ReturnsTokenServiceResponse()
        {
            var response = new LoginResponse("access", "new-refresh", DateTime.UtcNow, "patient@test.com", "Mona Adel", "Patient");
            _tokenService.RefreshTokenAsync("refresh-token", _ip, _ct).Returns(Result<LoginResponse>.Ok(response));

            var result = await _sut.RefreshTokenAsync(_request, _ip, _ct);

            Assert.True(result.IsSuccess);
            Assert.Equal(response, result.Value);
        }

        [Fact]
        public async Task RefreshTokenAsync_WhenTokenInvalid_ReturnsInvalidRefreshTokenError()
        {
            _tokenService.RefreshTokenAsync("refresh-token", _ip, _ct).Returns(Result<LoginResponse>.Fail(AuthErrors.InvalidRefreshToken));

            var result = await _sut.RefreshTokenAsync(_request, _ip, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AuthErrors.InvalidRefreshToken, result.Errors);
        }
    }
}
