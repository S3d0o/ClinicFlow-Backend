using ClinicFlow.IntegrationTests.Infrastructure;
using FluentAssertions;
using FluentValidation;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Presentation.Controllers.V2;
using Shared.Settings;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace ClinicFlow.IntegrationTests.Tests.Security
{
    // The /api/v2 conventions through HTTP (API_CONTRACT.md §13.1, P1 PR 0), on test-only endpoints
    // (ConventionsTestControllers.cs) added to a host of this class's own, next to the same endpoints on an inherited route
    [Collection(nameof(IntegrationTestCollection))]
    public class V2ConventionsTests : IAsyncLifetime
    {
        private const string V2Url = "/api/v2/test-conventions";
        private const string InheritedUrl = "/api/test-conventions";
        private const int PermitLimit = 2;

        private readonly ClinicFlowApiFactory _factory;
        private WebApplicationFactory<Program> _host = null!;
        private HttpClient _client = null!;

        public V2ConventionsTests(ClinicFlowApiFactory factory)
        {
            _factory = factory;
        }

        public async Task InitializeAsync()
        {
            await _factory.ResetDataBaseAsync();
            _host = CreateHost(_factory);
            _client = _host.CreateClient();
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, Guid.NewGuid().ToString());
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserRoleHeader, "Patient");
        }

        public async Task DisposeAsync() => await _host.DisposeAsync();

        // A host with the test controllers and their validator, and a low auth rate limit (only the "limited"
        // endpoints use it). Its own limiter starts empty for every test
        private static WebApplicationFactory<Program> CreateHost(ClinicFlowApiFactory factory) => factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.AddControllers().AddApplicationPart(typeof(V2ConventionsTestController).Assembly);
                services.AddScoped<IValidator<ConventionsInvoiceRequest>, ConventionsInvoiceRequestValidator>();
                services.PostConfigure<RateLimitSettings>(s => { s.PermitLimit = PermitLimit; s.WindowSeconds = 60; });
            }));

        private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
        {
            response.StatusCode.Should().Be(expected);
            response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            problem.GetProperty("status").GetInt32().Should().Be((int)expected);
            problem.TryGetProperty("traceId", out _).Should().BeTrue();
            return problem;
        }

        private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

        // ── The prefix ───────────────────────────────────────────────────────

        [Fact]
        public void V2Controllers_AreUnderApiV2_AndMarked()
        {
            // The same check AuthorizationTests runs on the real app, here with a V2 controller present
            var actions = _host.Services.GetRequiredService<EndpointDataSource>().Endpoints
                .OfType<RouteEndpoint>()
                .Where(e => e.Metadata.GetMetadata<ControllerActionDescriptor>() is not null)
                .ToList();

            var v2 = actions.Where(e => e.Metadata.GetMetadata<ApiV2Attribute>() is not null).ToList();
            v2.Should().NotBeEmpty();
            v2.Should().OnlyContain(e => e.RoutePattern.RawText!.StartsWith(V2ApiController.RoutePrefix + "/"));
            actions.Where(e => e.RoutePattern.RawText!.StartsWith(V2ApiController.RoutePrefix + "/"))
                .Should().OnlyContain(e => e.Metadata.GetMetadata<ApiV2Attribute>() != null);
        }

        // ── Ids are strings, times "HH:mm" ───────────────────────────────────

        [Fact]
        public async Task V2Response_IdsAreStrings_TimesAreHoursAndMinutes()
        {
            var response = await _client.GetAsync($"{V2Url}/ids");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("id").GetString().Should().Be("42");
            body.GetProperty("branchId").GetString().Should().Be("7");
            body.GetProperty("doctorId").ValueKind.Should().Be(JsonValueKind.Null);
            body.GetProperty("serviceIds").EnumerateArray().Select(e => e.GetString()).Should().Equal("1", "2");
            body.GetProperty("count").GetInt32().Should().Be(3);
            body.GetProperty("startTime").GetString().Should().Be("09:30");
            body.GetProperty("endTime").GetString().Should().Be("17:00");
        }

        [Fact]
        public async Task InheritedResponse_KeepsNumberIdsAndSeconds()
        {
            var response = await _client.GetAsync($"{InheritedUrl}/ids");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("id").GetInt32().Should().Be(42);
            body.GetProperty("serviceIds")[0].GetInt32().Should().Be(1);
            body.GetProperty("startTime").GetString().Should().Be("09:30:00");
        }

        [Fact]
        public async Task V2Request_ReadsIdsFromStrings_AndTimesWithOrWithoutSeconds()
        {
            var response = await _client.PostAsync($"{V2Url}/echo", Json(
                """{"id": "5", "branchId": "8", "doctorId": "9", "serviceIds": ["3", "4"], "count": 1, "startTime": "08:15", "endTime": "12:00:00"}"""));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("id").GetString().Should().Be("5");
            body.GetProperty("doctorId").GetString().Should().Be("9");
            body.GetProperty("serviceIds").EnumerateArray().Select(e => e.GetString()).Should().Equal("3", "4");
            body.GetProperty("startTime").GetString().Should().Be("08:15");
            body.GetProperty("endTime").GetString().Should().Be("12:00");
        }

        [Fact]
        public async Task V2Request_IdThatIsNotDigits_Is400()
        {
            var response = await _client.PostAsync($"{V2Url}/echo", Json(
                """{"id": "abc", "branchId": "8", "serviceIds": [], "count": 1, "startTime": "08:15"}"""));

            var problem = await ReadProblemAsync(response, HttpStatusCode.BadRequest);
            problem.TryGetProperty("errors", out _).Should().BeTrue();
        }

        // ── Errors with a code: shape A at any status ────────────────────────

        [Theory]
        [InlineData("validation", HttpStatusCode.BadRequest, "Consent.Required")]
        [InlineData("invalid-credentials", HttpStatusCode.BadRequest, "Auth.InvalidCredentials")]
        [InlineData("unauthorized", HttpStatusCode.Unauthorized, "Auth.InvalidRefreshToken")]
        [InlineData("forbidden", HttpStatusCode.Forbidden, "Auth.AccountNotActive")]
        [InlineData("not-found", HttpStatusCode.NotFound, "Branch.NotFound")]
        [InlineData("conflict", HttpStatusCode.Conflict, "Concurrency.Conflict")]
        [InlineData("too-many", HttpStatusCode.TooManyRequests, "Otp.ResendTooSoon")]
        public async Task V2CodedError_IsProblemDetailsWithTheCodeAsTitle(string kind, HttpStatusCode status, string code)
        {
            var problem = await ReadProblemAsync(await _client.GetAsync($"{V2Url}/errors/{kind}"), status);

            problem.GetProperty("title").GetString().Should().Be(code);
            problem.TryGetProperty("detail", out _).Should().BeTrue();
            problem.TryGetProperty("errors", out _).Should().BeFalse();
        }

        [Fact]
        public async Task InheritedCodedValidationError_StaysShapeC()
        {
            var problem = await ReadProblemAsync(await _client.GetAsync($"{InheritedUrl}/errors/validation"), HttpStatusCode.BadRequest);

            problem.GetProperty("errors").TryGetProperty("Consent.Required", out _).Should().BeTrue();
        }

        [Fact]
        public async Task InheritedInvalidCredentials_StaysA401()
            => await ReadProblemAsync(await _client.GetAsync($"{InheritedUrl}/errors/invalid-credentials"), HttpStatusCode.Unauthorized);

        [Fact]
        public async Task V2TooManyRequests_CarriesRetryAtAsAUtcInstant()
        {
            var problem = await ReadProblemAsync(await _client.GetAsync($"{V2Url}/errors/too-many"), HttpStatusCode.TooManyRequests);

            problem.GetProperty("retryAt").GetString().Should().Be("2026-10-30T12:00:00Z");
        }

        [Fact]
        public async Task V2WrongCode_CarriesAttemptsLeft()
        {
            var problem = await ReadProblemAsync(await _client.GetAsync($"{V2Url}/errors/attempts"), HttpStatusCode.BadRequest);

            problem.GetProperty("title").GetString().Should().Be("Otp.InvalidCode");
            problem.GetProperty("attemptsLeft").GetInt32().Should().Be(2);
        }

        [Fact]
        public async Task V2WithoutAToken_Is401WithoutACode()
        {
            // §13.1 "401": only token problems; the middleware's 401 is shape E
            _client.DefaultRequestHeaders.Clear();

            var problem = await ReadProblemAsync(await _client.GetAsync($"{V2Url}/ids"), HttpStatusCode.Unauthorized);

            problem.GetProperty("title").GetString().Should().Be("Unauthorized");
        }

        // ── Validation keys: camelCase on both ───────────────────────────────

        [Theory]
        [InlineData(V2Url)]
        [InlineData(InheritedUrl)]
        public async Task ValidationErrors_HaveCamelCasePathKeys(string url)
        {
            var response = await _client.PostAsJsonAsync($"{url}/invoices",
                new ConventionsInvoiceRequest(null, [new ConventionsLine(5), new ConventionsLine(0)]));

            var problem = await ReadProblemAsync(response, HttpStatusCode.BadRequest);
            var errors = problem.GetProperty("errors");
            errors.TryGetProperty("patientName", out _).Should().BeTrue();
            errors.GetProperty("lines[1].unitPrice")[0].GetString().Should().Be("Price must be positive.");
            errors.TryGetProperty("PatientName", out _).Should().BeFalse();
        }

        // ── Paging ───────────────────────────────────────────────────────────

        [Fact]
        public async Task Paged_Defaults_AreFirstPageOfTwentyWithTheTotal()
        {
            var response = await _client.GetAsync($"{V2Url}/paged");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Headers.GetValues("X-Total-Count").Should().Equal("45");
            (await response.Content.ReadFromJsonAsync<int[]>()).Should().Equal(Enumerable.Range(1, 20));
        }

        [Fact]
        public async Task Paged_LastPage_HasTheRest()
        {
            var response = await _client.GetAsync($"{V2Url}/paged?pageNumber=3&pageSize=20");

            (await response.Content.ReadFromJsonAsync<int[]>()).Should().Equal(Enumerable.Range(41, 5));
        }

        [Theory]
        [InlineData("pageNumber=0", "pageNumber")]
        [InlineData("pageSize=0", "pageSize")]
        [InlineData("pageSize=101", "pageSize")]
        public async Task Paged_OutOfRange_Is400OnTheField(string query, string key)
        {
            var problem = await ReadProblemAsync(await _client.GetAsync($"{V2Url}/paged?{query}"), HttpStatusCode.BadRequest);

            problem.GetProperty("errors").TryGetProperty(key, out _).Should().BeTrue();
        }

        [Fact]
        public async Task Paged_MaxPageSize_IsAllowed()
            => (await _client.GetAsync($"{V2Url}/paged?pageSize=100")).StatusCode.Should().Be(HttpStatusCode.OK);

        // ── Rate limits ──────────────────────────────────────────────────────

        [Fact]
        public async Task V2RateLimit_Is429WithRetryAtAndRetryAfter()
        {
            for (var i = 0; i < PermitLimit; i++)
                (await _client.GetAsync($"{V2Url}/limited")).StatusCode.Should().Be(HttpStatusCode.NoContent);

            var rejected = await _client.GetAsync($"{V2Url}/limited");

            var problem = await ReadProblemAsync(rejected, HttpStatusCode.TooManyRequests);
            problem.GetProperty("title").GetString().Should().Be("RateLimit.Exceeded");
            rejected.Headers.RetryAfter.Should().NotBeNull();
            var retryAt = problem.GetProperty("retryAt").GetDateTime();
            retryAt.Kind.Should().Be(DateTimeKind.Utc);
            retryAt.Should().BeAfter(DateTime.UtcNow.AddSeconds(-5)).And.BeBefore(DateTime.UtcNow.AddSeconds(65));
        }

        [Fact]
        public async Task InheritedRateLimit_HasNoRetryAt()
        {
            for (var i = 0; i < PermitLimit; i++)
                await _client.GetAsync($"{InheritedUrl}/limited");

            var problem = await ReadProblemAsync(await _client.GetAsync($"{InheritedUrl}/limited"), HttpStatusCode.TooManyRequests);

            problem.TryGetProperty("retryAt", out _).Should().BeFalse();
        }
    }
}
