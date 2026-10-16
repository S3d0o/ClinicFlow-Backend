using ClinicFlow.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Shared.DTOs.Auth;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClinicFlow.IntegrationTests.Tests.Auth
{
    [Collection(nameof(IntegrationTestCollection))]
    public class PasswordResetTests : IAsyncLifetime
    {
        private const string ForgotUrl = "/api/auth/forgot-password";
        private const string ResetUrl = "/api/auth/reset-password";
        private const string LoginUrl = "/api/auth/login";
        private const string RefreshUrl = "/api/auth/refresh-token";
        private const string NewPassword = "NewPass@5678";

        private Guid _userId;
        private string _email = string.Empty;
        private string _password = string.Empty;

        private readonly ClinicFlowApiFactory _factory;
        private readonly HttpClient _client;
        private readonly JsonSerializerOptions _json;

        public PasswordResetTests(ClinicFlowApiFactory factory)
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
            _factory.Emails.Clear();

            await _factory.SeedAsync(async db =>
                (_userId, _email, _password) = await SeedDataHelper.SeedPatientUserAsync(db));
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task ForgotPassword_KnownEmail_Returns204AndSendsResetEmail()
        {
            var response = await _client.PostAsJsonAsync(ForgotUrl, new ForgotPasswordRequest(_email));

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            _factory.Emails.Sent.Should().ContainSingle(e =>
                e.Kind == SentEmailKind.PasswordReset && e.To == _email && e.UserId == _userId && !string.IsNullOrEmpty(e.Token));
        }

        [Fact]
        public async Task ForgotPassword_UnknownEmail_Returns204AndSendsNothing()
        {
            var response = await _client.PostAsJsonAsync(ForgotUrl, new ForgotPasswordRequest("nobody@test.com"));

            // Same status as a known email, so callers can't discover registered emails
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            _factory.Emails.Sent.Should().BeEmpty();
        }

        [Fact]
        public async Task ForgotPassword_EmailSendFails_StillReturns204LikeAnUnknownEmail()
        {
            // A 500 here would only happen for registered emails and reveal them (REVIEW.md NL24)
            _factory.Emails.FailSends = true;

            var known = await _client.PostAsJsonAsync(ForgotUrl, new ForgotPasswordRequest(_email));
            var unknown = await _client.PostAsJsonAsync(ForgotUrl, new ForgotPasswordRequest("nobody@test.com"));

            known.StatusCode.Should().Be(HttpStatusCode.NoContent);
            unknown.StatusCode.Should().Be(HttpStatusCode.NoContent);
            _factory.Emails.Sent.Should().BeEmpty();
        }

        [Fact]
        public async Task ForgotPassword_SuspendedUser_Returns204AndSendsNothing()
        {
            await _factory.SeedAsync(async db =>
                await db.Users.Where(u => u.Id == _userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false)));

            var response = await _client.PostAsJsonAsync(ForgotUrl, new ForgotPasswordRequest(_email));

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            _factory.Emails.Sent.Should().BeEmpty();
        }

        [Fact]
        public async Task ResetPassword_TokenFromEmail_ChangesPasswordAndEndsExistingSessions()
        {
            var session = await LoginAsync(_password);
            var token = await RequestResetTokenAsync();

            var response = await _client.PostAsJsonAsync(ResetUrl, new ResetPasswordRequest(_userId.ToString(), token, NewPassword));

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            await _factory.SeedAsync(async db =>
            {
                var tokens = await db.RefreshTokens.AsNoTracking().Where(t => t.UserId == _userId).ToListAsync();
                tokens.Should().NotBeEmpty().And.OnlyContain(t => t.Revoked != null && t.RevocationReason == "Password reset");
            });

            (await _client.PostAsJsonAsync(RefreshUrl, new RefreshTokenRequest(session.RefreshToken)))
                .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await _client.PostAsJsonAsync(LoginUrl, new LoginRequest(_email, _password)))
                .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            await LoginAsync(NewPassword);
        }

        [Fact]
        public async Task ResetPassword_TokenUsedTwice_SecondAttemptReturns500()
        {
            var token = await RequestResetTokenAsync();
            (await _client.PostAsJsonAsync(ResetUrl, new ResetPasswordRequest(_userId.ToString(), token, NewPassword)))
                .StatusCode.Should().Be(HttpStatusCode.NoContent);

            // The reset rotates the security stamp, which invalidates the token
            var response = await _client.PostAsJsonAsync(ResetUrl, new ResetPasswordRequest(_userId.ToString(), token, "Another@9012"));

            response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
            (await response.Content.ReadAsStringAsync()).Should().Contain("Auth.InvalidPasswordResetToken");
            await LoginAsync(NewPassword);
        }

        [Fact]
        public async Task ResetPassword_InvalidToken_Returns500AndKeepsPassword()
        {
            var response = await _client.PostAsJsonAsync(ResetUrl, new ResetPasswordRequest(_userId.ToString(), "not-a-real-token", NewPassword));

            // 500 is the documented status today (REVIEW.md M3)
            response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
            (await response.Content.ReadAsStringAsync()).Should().Contain("Auth.InvalidPasswordResetToken");
            await LoginAsync(_password);
        }

        [Fact]
        public async Task ResetPassword_UnknownUser_Returns404()
        {
            var response = await _client.PostAsJsonAsync(ResetUrl, new ResetPasswordRequest(Guid.NewGuid().ToString(), "token", NewPassword));

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await response.Content.ReadAsStringAsync()).Should().Contain("Auth.UserNotFound");
        }

        // ----------------------------------------------------------------

        private async Task<string> RequestResetTokenAsync()
        {
            (await _client.PostAsJsonAsync(ForgotUrl, new ForgotPasswordRequest(_email)))
                .StatusCode.Should().Be(HttpStatusCode.NoContent);
            return _factory.Emails.Sent.Last(e => e.Kind == SentEmailKind.PasswordReset).Token!;
        }

        private async Task<LoginResponse> LoginAsync(string password)
        {
            var response = await _client.PostAsJsonAsync(LoginUrl, new LoginRequest(_email, password));
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            return (await response.Content.ReadFromJsonAsync<LoginResponse>(_json))!;
        }
    }
}
