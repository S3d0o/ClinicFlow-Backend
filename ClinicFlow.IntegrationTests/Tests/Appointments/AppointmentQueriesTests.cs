using ClinicFlow.Domain.Enums;
using ClinicFlow.IntegrationTests.Infrastructure;
using Domain.Entities.AppModule;
using Domain.Entities.Tenancy;
using Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Shared.DTOs.Appointment;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClinicFlow.IntegrationTests.Tests.Appointments
{
    // The appointment reads (AppointmentQueries, CQRS-lite): projected in SQL, so these run against the database
    [Collection(nameof(IntegrationTestCollection))]
    public class AppointmentQueriesTests(ClinicFlowApiFactory factory) : IAsyncLifetime
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

        private readonly HttpClient _client = factory.CreateClient();

        // Doctor A has a reviewed, completed visit in 3 days' time and a confirmed one in 5; doctor B has one of their own
        private Guid _patientA, _doctorA, _doctorB;
        private int _reviewedId, _laterId, _otherDoctorsId;
        private DateOnly _reviewedDate;

        public async Task InitializeAsync()
        {
            await factory.ResetDataBaseAsync();
            _client.DefaultRequestHeaders.Clear();

            await factory.SeedAsync(async db =>
            {
                int slotId;
                (_patientA, _doctorA, slotId, _reviewedId) = await SeedDataHelper.SeedBaseAppointmentAsync(db, AppointmentStatus.Completed, "a");
                (_, _doctorB, _, _otherDoctorsId) = await SeedDataHelper.SeedBaseAppointmentAsync(db, AppointmentStatus.Confirmed, "b");

                var reviewed = await db.Appointments.Include(a => a.Slot).SingleAsync(a => a.Id == _reviewedId);
                _reviewedDate = reviewed.Slot.Date;
                db.Reviews.Add(new Review
                {
                    AppointmentId = reviewed.Id, DoctorProfileId = reviewed.DoctorProfileId, PatientProfileId = reviewed.PatientProfileId,
                    Rating = 5, CreatedAt = DateTime.UtcNow, IsVisible = true,
                });

                var date = reviewed.Slot.Date.AddDays(2);
                var laterSlot = new AppointmentSlot
                {
                    BranchId = Branch.MainId, DoctorProfileId = reviewed.DoctorProfileId, DoctorScheduleId = reviewed.Slot.DoctorScheduleId,
                    Date = date, StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(10, 30),
                    StartsAtUtc = SeedDataHelper.SlotUtc(date, new TimeOnly(10, 0)), EndsAtUtc = SeedDataHelper.SlotUtc(date, new TimeOnly(10, 30)),
                    Status = SlotStatus.Booked,
                };
                db.AppointmentSlots.Add(laterSlot);
                await db.SaveChangesAsync();

                var later = Appointment.Book(laterSlot, reviewed.PatientProfileId, "Follow-up", DateTime.UtcNow);
                db.Appointments.Add(later);
                await db.SaveChangesAsync();
                _laterId = later.Id;
            });
        }

        public Task DisposeAsync() => Task.CompletedTask;

        // ── GET /api/appointments/{id} ───────────────────────────────────────

        [Fact]
        public async Task GetAppointment_ReturnsTheProjectedFields()
        {
            AuthenticateAs(_patientA, "Patient");

            var body = await GetAsync<AppointmentResponse>($"/api/appointments/{_reviewedId}");

            body.Id.Should().Be(_reviewedId);
            body.DoctorName.Should().Be("Test Doctor");
            body.PatientName.Should().Be("Test Patient");
            body.AppointmentDate.Should().Be(_reviewedDate);
            (body.StartTime, body.EndTime).Should().Be((new TimeOnly(10, 0), new TimeOnly(10, 30)));
            body.Status.Should().Be(AppointmentStatus.Completed);
            body.HasReview.Should().BeTrue(); // was always false here before PR 7: the review wasn't loaded
        }

        [Fact]
        public async Task GetAppointment_UnknownId_Returns404()
        {
            AuthenticateAs(Guid.NewGuid(), "Admin");

            (await _client.GetAsync("/api/appointments/999999")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task GetAppointment_AnotherDoctorsAppointment_Returns403()
        {
            AuthenticateAs(_doctorB, "Doctor");

            (await _client.GetAsync($"/api/appointments/{_reviewedId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        // ── GET /api/appointments/doctor/my ──────────────────────────────────

        [Fact]
        public async Task GetDoctorAppointments_ReturnsOnlyTheDoctorsOwn_NewestFirst()
        {
            AuthenticateAs(_doctorA, "Doctor");

            var body = await GetAsync<List<AppointmentResponse>>("/api/appointments/doctor/my");

            body.Select(a => a.Id).Should().Equal(_laterId, _reviewedId);
            body.Select(a => a.HasReview).Should().Equal(false, true); // the doctor list never loaded reviews before PR 7
            body.Should().NotContain(a => a.Id == _otherDoctorsId);
        }

        [Fact]
        public async Task GetDoctorAppointments_StatusFilter_ReturnsOnlyThatStatus()
        {
            AuthenticateAs(_doctorA, "Doctor");

            var body = await GetAsync<List<AppointmentResponse>>("/api/appointments/doctor/my?Status=Completed");

            body.Select(a => a.Id).Should().Equal(_reviewedId);
        }

        [Fact]
        public async Task GetDoctorAppointments_SecondPage_ReturnsTheOlderVisit()
        {
            AuthenticateAs(_doctorA, "Doctor");

            var body = await GetAsync<List<AppointmentResponse>>("/api/appointments/doctor/my?PageSize=1&PageNumber=2");

            body.Select(a => a.Id).Should().Equal(_reviewedId);
        }

        [Theory]
        [InlineData("CheckedIn")]
        [InlineData("InProgress")]
        [InlineData("Rescheduled")]
        public async Task GetDoctorAppointments_FilterByANewStatus_Returns200(string status)
        {
            // The new statuses (docs/v2/adr/0004) are valid filters; nothing creates them yet
            AuthenticateAs(_doctorA, "Doctor");

            var body = await GetAsync<List<AppointmentResponse>>($"/api/appointments/doctor/my?Status={status}");

            body.Should().BeEmpty();
        }

        [Fact]
        public async Task GetDoctorAppointments_WithoutADoctorProfile_Returns404()
        {
            AuthenticateAs(Guid.NewGuid(), "Doctor");

            (await _client.GetAsync("/api/appointments/doctor/my")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        // ── GET /api/appointments/my ─────────────────────────────────────────

        [Fact]
        public async Task GetPatientAppointments_WithoutAPatientProfile_Returns404()
        {
            AuthenticateAs(Guid.NewGuid(), "Patient");

            (await _client.GetAsync("/api/appointments/my")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task GetPatientAppointments_ReturnsBothVisits_WithTheirReviewFlags()
        {
            AuthenticateAs(_patientA, "Patient");

            var body = await GetAsync<List<AppointmentResponse>>("/api/appointments/my");

            body.Select(a => (a.Id, a.HasReview)).Should().Equal((_laterId, false), (_reviewedId, true));
        }

        private async Task<T> GetAsync<T>(string url)
        {
            var response = await _client.GetAsync(url);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            return (await response.Content.ReadFromJsonAsync<T>(Json))!;
        }

        private void AuthenticateAs(Guid userId, string role)
        {
            _client.DefaultRequestHeaders.Clear();
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId.ToString());
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserRoleHeader, role);
        }
    }
}
