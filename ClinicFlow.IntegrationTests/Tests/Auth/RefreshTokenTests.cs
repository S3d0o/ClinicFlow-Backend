using ClinicFlow.IntegrationTests.Infrastructure;
using FluentAssertions;
using Shared.DTOs.Auth;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClinicFlow.IntegrationTests.Tests.Auth
{
    [Collection(nameof(IntegrationTestCollection))]
    public class RefreshTokenTests : IAsyncLifetime
    {
        private const string LoginUrl = "/api/auth/login";
        private const string RefreshUrl = "/api/auth/refresh-token";

        private string _email = string.Empty;
        private string _password = string.Empty;

        private readonly ClinicFlowApiFactory _factory;
        private readonly HttpClient _client;

        public RefreshTokenTests(ClinicFlowApiFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        public async Task InitializeAsync()
        {
            await _factory.ResetDataBaseAsync();
            _client.DefaultRequestHeaders.Clear();

            await _factory.SeedAsync(async db =>
            {
                (_, _email, _password) =
                    await SeedDataHelper.SeedPatientUserAsync(db);
            });
        }

        public Task DisposeAsync() => Task.CompletedTask;

        // ----------------------------------------------------------------

        // Helper — login and return body
        private async Task<LoginResponse> LoginAsync()
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            options.Converters.Add(new JsonStringEnumConverter());

            var response = await _client.PostAsJsonAsync(
                LoginUrl, new LoginRequest(_email, _password));

            return (await response.Content.ReadFromJsonAsync<LoginResponse>(options))!;
        }

        [Fact]
        public async Task RefreshToken_ValidToken_Returns200WithNewTokens()
        {
            // Step 1 — get a real refresh token via login
            var loginBody = await LoginAsync();

            // Step 2 — exchange it
            var request = new RefreshTokenRequest(loginBody.RefreshToken);
            var response = await _client.PostAsJsonAsync(RefreshUrl, request);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            options.Converters.Add(new JsonStringEnumConverter());

            var body = await response.Content.ReadFromJsonAsync<LoginResponse>(options);

            body.Should().NotBeNull();
            body!.AccessToken.Should().NotBeNullOrEmpty();
            body.RefreshToken.Should().NotBeNullOrEmpty();

            // token rotation — new refresh token must differ from old one
            body.RefreshToken.Should().NotBe(loginBody.RefreshToken);
        }

        [Fact]
        public async Task RefreshToken_InvalidToken_Returns401()
        {
            // Arrange
            var request = new RefreshTokenRequest("invalid-refresh-token-xyz");

            // Act
            var response = await _client.PostAsJsonAsync(RefreshUrl, request);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }
}