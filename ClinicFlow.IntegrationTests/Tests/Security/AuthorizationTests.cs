using ClinicFlow.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Presentation.Authorization;
using Presentation.Controllers.V2;
using Shared.Authorization;
using Shared.DTOs.Specialty;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;

namespace ClinicFlow.IntegrationTests.Tests.Security
{
    [Collection(nameof(IntegrationTestCollection))]
    public class AuthorizationTests : IAsyncLifetime
    {
        private const string SpecialtyUrl = "/api/Specialty";
        private const string AppointmentUrl = "/api/appointments";

        private Guid _patientUserId;
        private Guid _doctorUserId;
        private Guid _otherPatientUserId;
        private int _appointmentId;

        private readonly ClinicFlowApiFactory _factory;
        private readonly HttpClient _client;

        public AuthorizationTests(ClinicFlowApiFactory factory)
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
                (_patientUserId, _doctorUserId, _, _appointmentId) =
                    await SeedDataHelper.SeedBaseAppointmentAsync(db);

                _otherPatientUserId = await SeedDataHelper.SeedPatientOnlyAsync(db);
            });
        }

        public Task DisposeAsync() => Task.CompletedTask;

        private void AuthenticateAs(Guid userId, string role)
        {
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId.ToString());
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserRoleHeader, role);
        }

        // ── Specialty writes are admin-only (C1) ───────────────────────────

        [Fact]
        public async Task CreateSpecialty_Anonymous_Returns401()
        {
            var response = await _client.PostAsJsonAsync(SpecialtyUrl, new SpecialtyRequest("Neurology", null, null));

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task CreateSpecialty_AsPatient_Returns403()
        {
            AuthenticateAs(_patientUserId, "Patient");

            var response = await _client.PostAsJsonAsync(SpecialtyUrl, new SpecialtyRequest("Neurology", null, null));

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        [Fact]
        public async Task DeleteSpecialty_Anonymous_Returns401()
        {
            var response = await _client.DeleteAsync($"{SpecialtyUrl}/1");

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        // ── Secure by default (NM5) ────────────────────────────────────────

        [Theory]
        [InlineData("/api/doctors")]
        [InlineData("/api/Specialty")]
        [InlineData("/api/reviews/doctor/1")]
        [InlineData("/health")]
        [InlineData("/health/live")]
        [InlineData("/health/ready")]
        public async Task PublicEndpoints_Anonymous_Return200(string url)
        {
            var response = await _client.GetAsync(url);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [Fact]
        public async Task UnknownRoute_Anonymous_Returns401()
        {
            // The fallback policy covers every request with no explicit rule, unknown routes included
            var response = await _client.GetAsync("/api/does-not-exist");

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public void EveryControllerAction_DeclaresAuthorizeOrAllowAnonymous()
        {
            // The fallback policy makes a forgotten attribute fail closed; this keeps every rule explicit (CLAUDE.md)
            var actions = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
                .Where(e => e.Metadata.GetMetadata<ControllerActionDescriptor>() is not null)
                .ToList();

            actions.Should().NotBeEmpty();
            actions.Where(e => e.Metadata.GetMetadata<IAuthorizeData>() is null && e.Metadata.GetMetadata<IAllowAnonymous>() is null)
                .Select(e => e.DisplayName)
                .Should().BeEmpty();
        }

        [Fact]
        public async Task UnknownV2Route_Anonymous_Returns401()
        {
            // /api/v2 is under the same fallback policy (contract §13.1 "401": no token is a 401 without a code)
            var response = await _client.GetAsync("/api/v2/does-not-exist");

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public void V2Routes_AreExactlyTheV2Controllers()
        {
            // An action under api/v2/ must come from a V2ApiController, so it gets the §13.1 conventions (ids as strings,
            // shape A errors, ...); a V2ApiController action must stay under the prefix. V2ConventionsTests runs the same
            // check on a host that has a V2 controller
            var actions = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
                .OfType<RouteEndpoint>()
                .Where(e => e.Metadata.GetMetadata<ControllerActionDescriptor>() is not null)
                .ToList();

            actions.Where(e => e.RoutePattern.RawText!.StartsWith(V2ApiController.RoutePrefix + "/") != (e.Metadata.GetMetadata<ApiV2Attribute>() is not null))
                .Select(e => e.DisplayName)
                .Should().BeEmpty();
        }

        // Every endpoint anyone can call without logging in (docs/v2/adr/0002 decision 4), /api/v2 included: a public V2
        // endpoint goes here by its full route ("POST api/v2/auth/login"). A new public endpoint fails this test until it
        // is added here on purpose; a removed one fails it until it is taken out
        private static readonly string[] AnonymousAllowlist =
        [
            "POST api/auth/register/patient",
            "POST api/auth/register/doctor",
            "POST api/auth/login",
            "POST api/auth/refresh-token",
            "POST api/auth/logout",
            "POST api/auth/confirm-email",
            "POST api/auth/forgot-password",
            "POST api/auth/reset-password",
            "GET api/doctors",
            "GET api/doctors/{id}",
            "GET api/doctors/{id}/slots",
            "GET api/doctors/{id}/reviews",
            "GET api/reviews/doctor/{doctorId}",
            "GET api/Specialty",
            "GET api/Specialty/{id}",
            "ANY health",
            "ANY health/live",
            "ANY health/ready",
        ];

        [Fact]
        public void AnonymousEndpoints_AreExactlyTheAllowlist()
        {
            var anonymous = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
                .OfType<RouteEndpoint>()
                .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
                .Select(e => $"{string.Join(",", e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? ["ANY"])} {e.RoutePattern.RawText!.TrimStart('/')}")
                .Where(e => !e.Contains(" openapi/") && !e.Contains(" scalar")) // API docs, mapped in Development only
                .ToList();

            anonymous.Should().BeEquivalentTo(AnonymousAllowlist);
        }

        [Theory]
        [InlineData("Admin", true)]
        [InlineData("Patient", false)]
        public async Task PermissionPolicies_AreWiredIntoTheApp(string role, bool allowed)
        {
            // No inherited route uses [HasPermission] yet (ADR 0002), so this checks the registration directly:
            // the app's policy provider builds the policy and the matrix decides (Admin holds staff.view)
            var policy = await _factory.Services.GetRequiredService<IAuthorizationPolicyProvider>()
                .GetPolicyAsync(PermissionPolicy.NameFor(Permissions.Staff.View));
            var user = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Role, role)], "Test"));

            var result = await _factory.Services.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, null, policy!);

            result.Succeeded.Should().Be(allowed);
        }

        // ── Appointment details are owner/admin only (C4) ──────────────────

        [Fact]
        public async Task GetAppointment_OwningPatient_Returns200()
        {
            AuthenticateAs(_patientUserId, "Patient");

            var response = await _client.GetAsync($"{AppointmentUrl}/{_appointmentId}");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [Fact]
        public async Task GetAppointment_OwningDoctor_Returns200()
        {
            AuthenticateAs(_doctorUserId, "Doctor");

            var response = await _client.GetAsync($"{AppointmentUrl}/{_appointmentId}");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [Fact]
        public async Task GetAppointment_OtherPatient_Returns403()
        {
            AuthenticateAs(_otherPatientUserId, "Patient");

            var response = await _client.GetAsync($"{AppointmentUrl}/{_appointmentId}");

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        [Fact]
        public async Task GetAppointment_Admin_Returns200()
        {
            AuthenticateAs(Guid.NewGuid(), "Admin");

            var response = await _client.GetAsync($"{AppointmentUrl}/{_appointmentId}");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }
}
