using ClinicFlow.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Services.Abstraction.Contracts;
using Shared.DTOs.Specialty;
using Domain.ResultPattern;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ClinicFlow.IntegrationTests.Tests.Security
{
    // REVIEW.md M5 and NL9: every error response is ProblemDetails with a status and a traceId
    [Collection(nameof(IntegrationTestCollection))]
    public class ProblemDetailsTests : IAsyncLifetime
    {
        private readonly ClinicFlowApiFactory _factory;
        private readonly HttpClient _client;

        public ProblemDetailsTests(ClinicFlowApiFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        public async Task InitializeAsync()
        {
            await _factory.ResetDataBaseAsync();
            _client.DefaultRequestHeaders.Clear();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
        {
            response.StatusCode.Should().Be(expected);
            response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            problem.GetProperty("status").GetInt32().Should().Be((int)expected);
            problem.TryGetProperty("traceId", out _).Should().BeTrue();
            return problem;
        }

        [Fact]
        public async Task UnknownRoute_Returns404ProblemDetails()
        {
            // Anonymous callers get 401 instead: the fallback policy also covers unknown routes (REVIEW.md NM5)
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, Guid.NewGuid().ToString());
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserRoleHeader, "Patient");

            await ReadProblemAsync(await _client.GetAsync("/api/does-not-exist"), HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task ProtectedEndpointWithoutAuthentication_Returns401ProblemDetails()
            => await ReadProblemAsync(await _client.GetAsync("/api/appointments/my"), HttpStatusCode.Unauthorized);

        [Fact]
        public async Task WrongRole_Returns403ProblemDetails()
        {
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, Guid.NewGuid().ToString());
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserRoleHeader, "Patient");

            await ReadProblemAsync(await _client.GetAsync("/api/admin/stats/overview"), HttpStatusCode.Forbidden);
        }

        [Fact]
        public async Task ValidatorFailure_Returns400WithErrorsAndTraceId()
        {
            // Used to be a bare ValidationProblemDetails with no status, type or traceId
            var response = await _client.PostAsJsonAsync("/api/auth/login", new { email = "", password = "" });

            var problem = await ReadProblemAsync(response, HttpStatusCode.BadRequest);
            problem.TryGetProperty("type", out _).Should().BeTrue();
            // camelCase on every route since P1 PR 0 (plan Q-F, contract §3.1 B); PascalCase before
            problem.GetProperty("errors").TryGetProperty("email", out _).Should().BeTrue();
            problem.GetProperty("errors").TryGetProperty("password", out _).Should().BeTrue();
            problem.GetProperty("errors").TryGetProperty("Email", out _).Should().BeFalse();
        }

        [Fact]
        public async Task ValidatorFailure_OnAnIdField_KeysItInCamelCase()
        {
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, Guid.NewGuid().ToString());
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserRoleHeader, "Patient");

            var response = await _client.PostAsJsonAsync("/api/appointments", new { slotId = 0 });

            var problem = await ReadProblemAsync(response, HttpStatusCode.BadRequest);
            problem.GetProperty("errors").TryGetProperty("slotId", out _).Should().BeTrue();
        }

        [Fact]
        public async Task BindingFailure_Returns400InTheSameShape()
        {
            var response = await _client.GetAsync("/api/doctors/1/slots");

            var problem = await ReadProblemAsync(response, HttpStatusCode.BadRequest);
            problem.TryGetProperty("errors", out _).Should().BeTrue();
        }

        [Fact]
        public async Task UnhandledException_Returns500ProblemDetails()
        {
            using var factory = _factory.WithWebHostBuilder(builder =>
                builder.ConfigureTestServices(services => services.AddScoped<ISpecialtyService, ThrowingSpecialtyService>()));
            using var client = factory.CreateClient();

            var response = await client.GetAsync("/api/Specialty");

            await ReadProblemAsync(response, HttpStatusCode.InternalServerError);
        }

        // Outside Development the 500 body carries no exception details; the test host runs in Development,
        // where the developer exception page adds them
        private sealed class ThrowingSpecialtyService : ISpecialtyService
        {
            public Task<Result<List<SpecialtyResponse>>> GetAllAsync(bool includeInactive, CancellationToken ct)
                => throw new InvalidOperationException("boom");
            public Task<Result<SpecialtyResponse>> GetByIdAsync(int id, CancellationToken ct) => throw new NotSupportedException();
            public Task<Result<SpecialtyResponse>> CreateAsync(SpecialtyRequest specialtyDTO, CancellationToken ct) => throw new NotSupportedException();
            public Task<Result<SpecialtyResponse>> UpdateAsync(int id, SpecialtyRequest specialtyDTO, CancellationToken ct) => throw new NotSupportedException();
            public Task<Result> DeleteByIdAsync(int id, CancellationToken ct) => throw new NotSupportedException();
        }
    }
}
