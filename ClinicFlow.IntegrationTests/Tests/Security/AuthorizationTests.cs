using ClinicFlow.IntegrationTests.Infrastructure;
using FluentAssertions;
using Shared.DTOs.Specialty;
using System.Net;
using System.Net.Http.Json;

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
