using ClinicFlow.Domain.Enums;
using ClinicFlow.IntegrationTests.Infrastructure;
using Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Shared.DTOs.Appointment;
using System.Net;
using System.Net.Http.Json;

namespace ClinicFlow.IntegrationTests.Tests.Appointments
{
    // Doctor and admin paths of cancel, plus complete and notes. The cancel endpoint picks the
    // service method from the caller's role, which only an HTTP-level test can check
    [Collection(nameof(IntegrationTestCollection))]
    public class AppointmentStaffActionsTests : IAsyncLifetime
    {
        private const string BaseUrl = "/api/appointments";

        private Guid _patientUserId;
        private Guid _doctorUserId;
        private Guid _otherDoctorUserId;
        private int _slotId;
        private int _appointmentId;
        private readonly Guid _adminUserId = Guid.NewGuid();

        private readonly ClinicFlowApiFactory _factory;
        private readonly HttpClient _client;

        public AppointmentStaffActionsTests(ClinicFlowApiFactory factory)
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
                (_patientUserId, _doctorUserId, _slotId, _appointmentId) =
                    await SeedDataHelper.SeedBaseAppointmentAsync(db, AppointmentStatus.Confirmed);
                (_, _otherDoctorUserId, _, _) = await SeedDataHelper.SeedBaseAppointmentAsync(db, AppointmentStatus.Confirmed, "2");

                // A confirmed appointment holds its slot
                var slot = await db.AppointmentSlots.SingleAsync(s => s.Id == _slotId);
                slot.Status = SlotStatus.Booked;
                await db.SaveChangesAsync();
            });
        }

        public Task DisposeAsync() => Task.CompletedTask;

        // ---- cancel ----

        [Fact]
        public async Task Cancel_AsOwningDoctor_Returns204FreesSlotAndNotifiesBothParties()
        {
            AuthenticateAs(_doctorUserId, "Doctor");

            var response = await _client.PostAsJsonAsync($"{BaseUrl}/{_appointmentId}/cancel", new CancelAppointmentRequest("Doctor is ill"));

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            await _factory.SeedAsync(async db =>
            {
                var appointment = await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == _appointmentId);
                appointment.Status.Should().Be(AppointmentStatus.Cancelled);
                appointment.CancelledBy.Should().Be(CancelledBy.Doctor);
                appointment.CancellationReason.Should().Be("Doctor is ill");
                (await db.AppointmentSlots.AsNoTracking().SingleAsync(s => s.Id == _slotId)).Status.Should().Be(SlotStatus.Available);

                var notified = await db.Notifications.AsNoTracking()
                    .Where(n => n.RelatedEntityId == _appointmentId && n.Type == NotificationType.AppointmentCancelled)
                    .Select(n => n.UserId).ToListAsync();
                notified.Should().BeEquivalentTo(new[] { _patientUserId, _doctorUserId });
            });
        }

        [Fact]
        public async Task Cancel_AsAnotherDoctor_Returns403AndLeavesAppointmentConfirmed()
        {
            AuthenticateAs(_otherDoctorUserId, "Doctor");

            var response = await _client.PostAsJsonAsync($"{BaseUrl}/{_appointmentId}/cancel", new CancelAppointmentRequest(null));

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await response.Content.ReadAsStringAsync()).Should().Contain("Appointment.Unauthorized");
            await AssertStatusAsync(AppointmentStatus.Confirmed);
        }

        [Fact]
        public async Task Cancel_AsAdmin_AnyAppointment_Returns204WithCancelledByAdmin()
        {
            AuthenticateAs(_adminUserId, "Admin");

            var response = await _client.PostAsJsonAsync($"{BaseUrl}/{_appointmentId}/cancel", new CancelAppointmentRequest("Clinic closed"));

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            await _factory.SeedAsync(async db =>
            {
                var appointment = await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == _appointmentId);
                appointment.Status.Should().Be(AppointmentStatus.Cancelled);
                appointment.CancelledBy.Should().Be(CancelledBy.Admin);
                (await db.AppointmentSlots.AsNoTracking().SingleAsync(s => s.Id == _slotId)).Status.Should().Be(SlotStatus.Available);
            });
        }

        [Fact]
        public async Task Cancel_AsAdmin_CompletedAppointment_Returns409AlreadyCancelled()
        {
            // Documents REVIEW.md M14 and the contract's error catalog: the wrong code, but the documented one
            await SetStatusAsync(AppointmentStatus.Completed);
            AuthenticateAs(_adminUserId, "Admin");

            var response = await _client.PostAsJsonAsync($"{BaseUrl}/{_appointmentId}/cancel", new CancelAppointmentRequest(null));

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await response.Content.ReadAsStringAsync()).Should().Contain("Appointment.AlreadyCancelled");
            await AssertStatusAsync(AppointmentStatus.Completed);
        }

        [Fact]
        public async Task Cancel_AsDoctor_AlreadyCancelled_Returns409()
        {
            await SetStatusAsync(AppointmentStatus.Cancelled);
            AuthenticateAs(_doctorUserId, "Doctor");

            var response = await _client.PostAsJsonAsync($"{BaseUrl}/{_appointmentId}/cancel", new CancelAppointmentRequest(null));

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await response.Content.ReadAsStringAsync()).Should().Contain("Appointment.AlreadyCancelled");
        }

        // ---- complete ----

        [Fact]
        public async Task Complete_AsOwningDoctor_Returns204AndNotifiesThePatient()
        {
            AuthenticateAs(_doctorUserId, "Doctor");

            var response = await _client.PostAsync($"{BaseUrl}/{_appointmentId}/complete", null);

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            await AssertStatusAsync(AppointmentStatus.Completed);
            await _factory.SeedAsync(async db =>
                (await db.Notifications.AsNoTracking()
                    .Where(n => n.RelatedEntityId == _appointmentId && n.Type == NotificationType.AppointmentCompleted)
                    .Select(n => n.UserId).ToListAsync())
                .Should().Equal(_patientUserId));
        }

        [Fact]
        public async Task Complete_AsAnotherDoctor_Returns403()
        {
            AuthenticateAs(_otherDoctorUserId, "Doctor");

            var response = await _client.PostAsync($"{BaseUrl}/{_appointmentId}/complete", null);

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            await AssertStatusAsync(AppointmentStatus.Confirmed);
        }

        [Fact]
        public async Task Complete_CancelledAppointment_Returns500CannotCompleteCancelled()
        {
            // 500 is the documented status today (REVIEW.md M3)
            await SetStatusAsync(AppointmentStatus.Cancelled);
            AuthenticateAs(_doctorUserId, "Doctor");

            var response = await _client.PostAsync($"{BaseUrl}/{_appointmentId}/complete", null);

            response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
            (await response.Content.ReadAsStringAsync()).Should().Contain("Appointment.CannotCompleteCancelled");
        }

        [Theory]
        [InlineData("Patient")]
        [InlineData("Admin")]
        public async Task Complete_AsNonDoctor_Returns403(string role)
        {
            AuthenticateAs(role == "Patient" ? _patientUserId : _adminUserId, role);

            var response = await _client.PostAsync($"{BaseUrl}/{_appointmentId}/complete", null);

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            await AssertStatusAsync(AppointmentStatus.Confirmed);
        }

        // ---- notes ----

        [Fact]
        public async Task Notes_AsOwningDoctor_Returns204AndReplacesNotes()
        {
            AuthenticateAs(_doctorUserId, "Doctor");

            var response = await _client.PutAsJsonAsync($"{BaseUrl}/{_appointmentId}/notes", new { notes = "Follow up in two weeks" });

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            await _factory.SeedAsync(async db =>
                (await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == _appointmentId)).DoctorNotes
                .Should().Be("Follow up in two weeks"));
        }

        [Fact]
        public async Task Notes_AsAnotherDoctor_Returns403AndLeavesNotesEmpty()
        {
            AuthenticateAs(_otherDoctorUserId, "Doctor");

            var response = await _client.PutAsJsonAsync($"{BaseUrl}/{_appointmentId}/notes", new { notes = "Not my patient" });

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            await _factory.SeedAsync(async db =>
                (await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == _appointmentId)).DoctorNotes.Should().BeNull());
        }

        [Fact]
        public async Task Notes_CancelledAppointment_Returns500DoctorNotesNotAllowed()
        {
            // 500 is the documented status today (REVIEW.md M3)
            await SetStatusAsync(AppointmentStatus.Cancelled);
            AuthenticateAs(_doctorUserId, "Doctor");

            var response = await _client.PutAsJsonAsync($"{BaseUrl}/{_appointmentId}/notes", new { notes = "Too late" });

            response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
            (await response.Content.ReadAsStringAsync()).Should().Contain("Appointment.DoctorNotesNotAllowed");
        }

        // ----------------------------------------------------------------

        private Task SetStatusAsync(AppointmentStatus status) => _factory.SeedAsync(async db =>
            await db.Appointments.Where(a => a.Id == _appointmentId)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, status)));

        private Task AssertStatusAsync(AppointmentStatus expected) => _factory.SeedAsync(async db =>
            (await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == _appointmentId)).Status.Should().Be(expected));

        private void AuthenticateAs(Guid userId, string role)
        {
            _client.DefaultRequestHeaders.Clear();
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId.ToString());
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserRoleHeader, role);
        }
    }
}
