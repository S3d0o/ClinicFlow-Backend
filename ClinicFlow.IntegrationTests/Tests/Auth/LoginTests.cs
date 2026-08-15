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
    public class LoginTests : IAsyncLifetime
    {
        private const string BaseUrl = "/api/auth/login";

        private string _email = string.Empty;
        private string _password = string.Empty;

        private readonly ClinicFlowApiFactory _factory;
        private readonly HttpClient _client;

        public LoginTests(ClinicFlowApiFactory factory)
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

        [Fact]
        public async Task Login_ValidCredentials_Returns200WithTokens()
        {
            // Arrange
            var request = new LoginRequest(_email, _password);

            // Act
            var response = await _client.PostAsJsonAsync(BaseUrl, request);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            options.Converters.Add(new JsonStringEnumConverter());

            var body = await response.Content.ReadFromJsonAsync<LoginResponse>(options);

            body.Should().NotBeNull();
            body!.AccessToken.Should().NotBeNullOrEmpty();
            body.RefreshToken.Should().NotBeNullOrEmpty();
            body.Email.Should().Be(_email);
            body.Role.Should().Be("Patient");
        }

        [Fact]
        public async Task Login_WrongPassword_Returns401()
        {
            // Arrange
            var request = new LoginRequest(_email, "WrongPassword@999!");

            // Act
            var response = await _client.PostAsJsonAsync(BaseUrl, request);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task Login_EmailNotFound_Returns401()
        {
            // Arrange
            var request = new LoginRequest("notexist@test.com", _password);

            // Act
            var response = await _client.PostAsJsonAsync(BaseUrl, request);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }
}