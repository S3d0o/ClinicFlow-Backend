using ClinicFlow.Domain.Enums;
using ClinicFlow.IntegrationTests.Infrastructure;
using Domain.Entities.AppModule;
using Domain.Entities.IdentityModule;
using Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Shared.DTOs.Appointment;
using Shared.Enums;
using System.Net;
using System.Net.Http.Json;

namespace ClinicFlow.IntegrationTests.Tests.Appointments
{
    [Collection(nameof(IntegrationTestCollection))]
    public class CancelAppointmentByPatientTests : IAsyncLifetime
    {
        private const string BaseUrl = "/api/appointments";

        private Guid _patientUserId;
        private Guid _doctorUserId;
        private int _appointmentId;
        private int _cancelledAppointmentId; // already cancelled — for that specific test
        private int _slotId;

        private readonly ClinicFlowApiFactory _factory;
        private readonly HttpClient _client;


        public CancelAppointmentByPatientTests(ClinicFlowApiFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }
        public Task DisposeAsync() => Task.CompletedTask;

        public async Task InitializeAsync()
        {
            await _factory.ResetDataBaseAsync();

            _client.DefaultRequestHeaders.Clear();

            await _factory.SeedAsync(async db =>
            {
                // confirmed appointment
                (_patientUserId, _, _slotId, _appointmentId) =
                    await SeedDataHelper.SeedBaseAppointmentAsync(db);

                // already cancelled appointment (second seed — different appointment, same DB)
                (_, _, _, _cancelledAppointmentId) =
                    await SeedDataHelper.SeedBaseAppointmentAsync(db, AppointmentStatus.Cancelled,"2");
            });
        }
        [Fact]
        public async Task CancelAppointment_ValidRequest_Returns200()
        {

            // Arrange
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, _patientUserId.ToString());
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserRoleHeader, "Patient");

            var request = new CancelAppointmentRequest("Patient is feeling better and no longer needs the appointment.");

            // Act
            var response = await _client.PostAsJsonAsync($"{BaseUrl}/{_appointmentId}/cancel", request);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        [Fact]
        public async Task CancelAppointment_AlreadyCancelled_Returns400()
        {

            // Arrange
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, _patientUserId.ToString());
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserRoleHeader, "Patient");

            var request = new CancelAppointmentRequest("Patient is feeling better and no longer needs the appointment.");

            // Act
            var response = await _client.PostAsJsonAsync($"{BaseUrl}/{_cancelledAppointmentId}/cancel", request);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        [Fact]
        public async Task CancelAppoitment_NotFound_Returns404()
        {
            // Arrange
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, _patientUserId.ToString());
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserRoleHeader, "Patient");
            var request = new CancelAppointmentRequest("Patient is feeling better and no longer needs the appointment.");
            // Act
            var response = await _client.PostAsJsonAsync($"{BaseUrl}/{999}/cancel", request);
            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task CancelAppoitment_Unauthenticated_Returns401()
        {
            // Arrange
            var request = new CancelAppointmentRequest("Patient is feeling better and no longer needs the appointment.");
            // Act
            var response = await _client.PostAsJsonAsync($"{BaseUrl}/{_cancelledAppointmentId}/cancel", request);
            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task CancelAppointment_WrongRole_Returns403()
        {
            // Arrange
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, _patientUserId.ToString());
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserRoleHeader, "NewRole");
            var request = new CancelAppointmentRequest("Patient is feeling better and no longer needs the appointment.");
            // Act
            var response = await _client.PostAsJsonAsync($"{BaseUrl}/{1}/cancel", request);
            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        }
    }
}
