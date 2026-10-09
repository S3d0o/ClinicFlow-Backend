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
    public class AuthSecurityTests : IAsyncLifetime
    {
        private const string LoginUrl = "/api/auth/login";
        private const string RefreshUrl = "/api/auth/refresh-token";
        private const string LogoutUrl = "/api/auth/logout";

        private Guid _userId;
        private string _email = string.Empty;
        private string _password = string.Empty;

        private readonly ClinicFlowApiFactory _factory;
        private readonly HttpClient _client;
        private readonly JsonSerializerOptions _json;

        public AuthSecurityTests(ClinicFlowApiFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
            _json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            _json.Converters.Add(new JsonStringEnumConverter());
        }

        public async Task InitializeAsync()
        {
            await _factory.ResetDataBaseAsync();
            _client.DefaultRequestHeaders.Clear();

            await _factory.SeedAsync(async db =>
            {
                (_userId, _email, _password) = await SeedDataHelper.SeedPatientUserAsync(db);

                // Users created through UserManager get LockoutEnabled = true; the helper builds them by hand
                var user = db.Users.Single(u => u.Id == _userId);
                user.LockoutEnabled = true;
                await db.SaveChangesAsync();
            });
        }

        public Task DisposeAsync() => Task.CompletedTask;

        // ----------------------------------------------------------------

        private async Task<LoginResponse> LoginAsync()
        {
            var response = await _client.PostAsJsonAsync(LoginUrl, new LoginRequest(_email, _password));
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            return (await response.Content.ReadFromJsonAsync<LoginResponse>(_json))!;
        }

        private async Task<HttpResponseMessage> RefreshAsync(string refreshToken)
            => await _client.PostAsJsonAsync(RefreshUrl, new RefreshTokenRequest(refreshToken));

        private static async Task<string?> GetProblemTitleAsync(HttpResponseMessage response)
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.TryGetProperty("title", out var title) ? title.GetString() : null;
        }

        [Fact]
        public async Task Login_AfterFiveWrongPasswords_ReturnsLockedOutEvenWithCorrectPassword()
        {
            // Arrange
            for (var i = 0; i < 5; i++)
            {
                var failed = await _client.PostAsJsonAsync(LoginUrl, new LoginRequest(_email, "WrongPassword@999!"));
                failed.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            }

            // Act
            var response = await _client.PostAsJsonAsync(LoginUrl, new LoginRequest(_email, _password));

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await GetProblemTitleAsync(response)).Should().Be("Auth.AccountLockedOut");
        }

        [Fact]
        public async Task RefreshToken_UserSuspended_Returns401()
        {
            // Arrange
            var login = await LoginAsync();
            await _factory.SeedAsync(async db =>
            {
                db.Users.Single(u => u.Id == _userId).IsActive = false;
                await db.SaveChangesAsync();
            });

            // Act
            var response = await RefreshAsync(login.RefreshToken);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task RefreshToken_RotatedTokenReusedWithinWindowFromSameIp_Returns401AndKeepsNewToken()
        {
            // Arrange — rotate once (client race scenario: the old token is sent again right away)
            var login = await LoginAsync();
            var refreshed = await (await RefreshAsync(login.RefreshToken)).Content.ReadFromJsonAsync<LoginResponse>(_json);

            // Act
            var reuse = await RefreshAsync(login.RefreshToken);
            var next = await RefreshAsync(refreshed!.RefreshToken);

            // Assert
            reuse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            next.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [Fact]
        public async Task RefreshToken_RotatedTokenReusedOutsideWindow_RevokesNewerTokens()
        {
            // Arrange — rotate once, then age the rotation so the reuse is treated as theft
            var login = await LoginAsync();
            var refreshed = await (await RefreshAsync(login.RefreshToken)).Content.ReadFromJsonAsync<LoginResponse>(_json);

            await _factory.SeedAsync(async db =>
            {
                var rotated = db.RefreshTokens.Single(t => t.UserId == _userId && t.ReplacedByTokenHash != null);
                rotated.LastUsed = DateTime.UtcNow.AddHours(-1);
                await db.SaveChangesAsync();
            });

            // Act
            var reuse = await RefreshAsync(login.RefreshToken);
            var next = await RefreshAsync(refreshed!.RefreshToken);

            // Assert — the attacker's (or victim's) newer token no longer works
            reuse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            next.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task Logout_OwnRefreshToken_Returns204()
        {
            // Arrange
            var login = await LoginAsync();
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, _userId.ToString());
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserRoleHeader, "Patient");

            // Act
            var response = await _client.PostAsJsonAsync(LogoutUrl, new RefreshTokenRequest(login.RefreshToken));

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await RefreshAsync(login.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task Logout_SomeoneElsesRefreshToken_Returns401AndTokenStillWorks()
        {
            // Arrange
            var login = await LoginAsync();
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, Guid.NewGuid().ToString());
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserRoleHeader, "Patient");

            // Act
            var response = await _client.PostAsJsonAsync(LogoutUrl, new RefreshTokenRequest(login.RefreshToken));

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            _client.DefaultRequestHeaders.Clear();
            (await RefreshAsync(login.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }
}
