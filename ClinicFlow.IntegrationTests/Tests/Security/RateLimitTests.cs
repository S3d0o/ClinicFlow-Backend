using ClinicFlow.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.DTOs.Auth;
using Shared.Settings;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ClinicFlow.IntegrationTests.Tests.Security
{
    // REVIEW.md M6: the credential and email endpoints are rate limited per client IP
    [Collection(nameof(IntegrationTestCollection))]
    public class RateLimitTests : IAsyncLifetime
    {
        private const int PermitLimit = 2;
        private readonly ClinicFlowApiFactory _factory;

        public RateLimitTests(ClinicFlowApiFactory factory)
        {
            _factory = factory;
        }

        public Task InitializeAsync() => _factory.ResetDataBaseAsync();

        public Task DisposeAsync() => Task.CompletedTask;

        // A separate host, so its limiter starts empty and the shared factory keeps its raised limit
        private WebApplicationFactory<Program> CreateLimitedFactory() => _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Seeding:DemoData", "false");
            builder.ConfigureServices(services =>
                services.PostConfigure<RateLimitSettings>(s => { s.PermitLimit = PermitLimit; s.WindowSeconds = 60; }));
        });

        [Fact]
        public async Task Login_MoreRequestsThanTheLimit_Returns429ProblemDetailsWithRetryAfter()
        {
            await using var limited = CreateLimitedFactory();
            var client = limited.CreateClient();
            var login = new LoginRequest("nobody@test.com", "Wrong@1234");

            for (var i = 0; i < PermitLimit; i++)
                (await client.PostAsJsonAsync("/api/auth/login", login)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            var rejected = await client.PostAsJsonAsync("/api/auth/login", login);

            rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
            rejected.Headers.RetryAfter.Should().NotBeNull();
            rejected.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
            var problem = await rejected.Content.ReadFromJsonAsync<JsonElement>();
            problem.GetProperty("title").GetString().Should().Be("RateLimit.Exceeded");
            problem.GetProperty("status").GetInt32().Should().Be(429);
            problem.TryGetProperty("traceId", out _).Should().BeTrue();
        }

        [Fact]
        public async Task Login_RateLimitedFromTheFrontendOrigin_ExposesRetryAfterToTheBrowser()
        {
            // A cross-origin frontend can only read Retry-After if CORS exposes it (contract 3.1.2)
            await using var limited = CreateLimitedFactory();
            var client = limited.CreateClient();
            var origin = (limited.Services.GetRequiredService<IConfiguration>()["Frontend:BaseUrl"] ?? "http://localhost:3000").TrimEnd('/');
            client.DefaultRequestHeaders.Add("Origin", origin);
            var login = new LoginRequest("nobody@test.com", "Wrong@1234");

            for (var i = 0; i < PermitLimit; i++)
                await client.PostAsJsonAsync("/api/auth/login", login);
            var rejected = await client.PostAsJsonAsync("/api/auth/login", login);

            rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
            rejected.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal(origin);
            rejected.Headers.GetValues("Access-Control-Expose-Headers").SelectMany(v => v.Split(','))
                .Select(h => h.Trim()).Should().Contain("Retry-After");
        }

        [Fact]
        public async Task AuthEndpoints_ShareOneLimitPerClient()
        {
            // Spreading attempts over login, forgot-password and registration doesn't get around the limit
            await using var limited = CreateLimitedFactory();
            var client = limited.CreateClient();

            (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("nobody@test.com", "Wrong@1234")))
                .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest("nobody@test.com")))
                .StatusCode.Should().Be(HttpStatusCode.NoContent);

            (await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest("nobody@test.com")))
                .StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        }

        [Fact]
        public async Task RefreshToken_IsNotRateLimited()
        {
            // Refresh and logout stay unlimited: staff behind one clinic IP must not lose their sessions
            await using var limited = CreateLimitedFactory();
            var client = limited.CreateClient();

            for (var i = 0; i < PermitLimit + 3; i++)
                (await client.PostAsJsonAsync("/api/auth/refresh-token", new RefreshTokenRequest("not-a-real-token")))
                    .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }
}
