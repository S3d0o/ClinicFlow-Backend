using ClinicFlow.Domain.Enums;
using ClinicFlow.IntegrationTests.Infrastructure;
using Domain.Entities.AppModule;
using Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Shared.DTOs.Doctor;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClinicFlow.IntegrationTests.Tests.Doctors
{
    [Collection(nameof(IntegrationTestCollection))]
    public class DoctorTests : IAsyncLifetime
    {
        private const string BaseUrl = "/api/doctors";

        private readonly ClinicFlowApiFactory _factory;
        private readonly HttpClient _client;
        private readonly JsonSerializerOptions _json;

        public DoctorTests(ClinicFlowApiFactory factory)
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
        }

        public Task DisposeAsync() => Task.CompletedTask;

        // ---- search ----

        [Fact]
        public async Task Search_ReturnsOnlyApprovedDoctorsWithActiveAccounts()
        {
            int visibleId = 0;
            await _factory.SeedAsync(async db =>
            {
                (_, visibleId) = await SeedDataHelper.SeedDoctorAsync(db, "visible");
                await SeedDataHelper.SeedDoctorAsync(db, "pending", approved: false);
                await SeedDataHelper.SeedDoctorAsync(db, "suspended", active: false);
            });

            var doctors = await SearchAsync("");

            doctors.Select(d => d.Id).Should().Equal(visibleId);
        }

        [Fact]
        public async Task Search_SpecialtyAndCityFilters_ReturnOnlyMatchingDoctors()
        {
            int cairoCardiologistId = 0, cardiologySpecialtyId = 0;
            await _factory.SeedAsync(async db =>
            {
                (_, cairoCardiologistId) = await SeedDataHelper.SeedDoctorAsync(db, "cairocardio", specialtyName: "Cardiology", city: "Cairo");
                await SeedDataHelper.SeedDoctorAsync(db, "gizacardio", specialtyName: "Cardiology", city: "Giza");
                await SeedDataHelper.SeedDoctorAsync(db, "cairoderm", specialtyName: "Dermatology", city: "Cairo");
                cardiologySpecialtyId = db.Specialties.Single(s => s.Name == "Cardiology").Id;
            });

            // The city filter is a case-insensitive substring match
            var doctors = await SearchAsync($"?specialtyId={cardiologySpecialtyId}&city=cai");

            doctors.Select(d => d.Id).Should().Equal(cairoCardiologistId);
            doctors.Single().SpecialtyName.Should().Be("Cardiology");
        }

        [Fact]
        public async Task Search_SortByFee_ReturnsCheapestFirst()
        {
            await _factory.SeedAsync(async db =>
            {
                await SeedDataHelper.SeedDoctorAsync(db, "mid", fee: 300);
                await SeedDataHelper.SeedDoctorAsync(db, "high", fee: 900);
                await SeedDataHelper.SeedDoctorAsync(db, "low", fee: 150);
            });

            var doctors = await SearchAsync("?sortBy=Fee");

            doctors.Select(d => d.ConsultationFee).Should().Equal(150, 300, 900);
        }

        // ---- slots ----

        [Fact]
        public async Task Slots_ReturnOnlyAvailableSlotsOrderedByStartTime()
        {
            int doctorId = 0;
            List<int> slotIds = [];
            await _factory.SeedAsync(async db =>
            {
                (_, doctorId) = await SeedDataHelper.SeedDoctorAsync(db, "slots");
                (_, slotIds) = await SeedDataHelper.SeedScheduleWithSlotsAsync(db, doctorId, DayOfWeek.Monday,
                    (3, new TimeOnly(11, 0), SlotStatus.Available),
                    (3, new TimeOnly(9, 0), SlotStatus.Available),
                    (3, new TimeOnly(10, 0), SlotStatus.Booked),
                    (3, new TimeOnly(12, 0), SlotStatus.Blocked),
                    (4, new TimeOnly(9, 0), SlotStatus.Available)); // another day
            });
            var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)).ToString("yyyy-MM-dd");

            var slots = await _client.GetFromJsonAsync<List<SlotResponse>>($"{BaseUrl}/{doctorId}/slots?date={date}", _json);

            slots!.Select(s => s.Id).Should().Equal(slotIds[1], slotIds[0]);
            slots.Should().OnlyContain(s => s.Status == SlotStatus.Available);
        }

        // ---- profile ----

        [Fact]
        public async Task UpdateProfile_OwnProfile_Returns204AndKeepsOmittedTextFields()
        {
            Guid userId = default;
            await _factory.SeedAsync(async db =>
            {
                (userId, var profileId) = await SeedDataHelper.SeedDoctorAsync(db, "profile", city: "Giza");
                var profile = await db.DoctorProfiles.SingleAsync(d => d.Id == profileId);
                profile.Bio = "Old bio";
                await db.SaveChangesAsync();
            });
            AuthenticateAs(userId, "Doctor");

            var response = await _client.PutAsJsonAsync($"{BaseUrl}/profile", new UpdateDoctorProfileRequest { ConsultationFee = 450m, ClinicCity = "Cairo" });

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            await _factory.SeedAsync(async db =>
            {
                var profile = await db.DoctorProfiles.AsNoTracking().SingleAsync(d => d.UserId == userId);
                profile.ConsultationFee.Should().Be(450m);
                profile.ClinicCity.Should().Be("Cairo");
                profile.Bio.Should().Be("Old bio");
                profile.IsApprovedByAdmin.Should().BeTrue();
            });
        }

        [Fact]
        public async Task UpdateProfile_WithoutFee_Returns400AndChangesNothing()
        {
            // M15: the fee is required today, which is what keeps it from being overwritten with 0
            Guid userId = default;
            await _factory.SeedAsync(async db => (userId, _) = await SeedDataHelper.SeedDoctorAsync(db, "nofee", fee: 300));
            AuthenticateAs(userId, "Doctor");

            var response = await _client.PutAsJsonAsync($"{BaseUrl}/profile", new { bio = "Only the bio" });

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            await _factory.SeedAsync(async db =>
                (await db.DoctorProfiles.AsNoTracking().SingleAsync(d => d.UserId == userId)).ConsultationFee.Should().Be(300m));
        }

        // ---- delete schedule ----

        [Fact]
        public async Task DeleteSchedule_OwnScheduleWithoutAppointments_Returns204AndRemovesItsSlots()
        {
            Guid userId = default;
            int scheduleId = 0;
            await _factory.SeedAsync(async db =>
            {
                (userId, var doctorId) = await SeedDataHelper.SeedDoctorAsync(db, "delete");
                (scheduleId, _) = await SeedDataHelper.SeedScheduleWithSlotsAsync(db, doctorId, DayOfWeek.Monday,
                    (3, new TimeOnly(9, 0), SlotStatus.Available), (3, new TimeOnly(9, 30), SlotStatus.Available));
            });
            AuthenticateAs(userId, "Doctor");

            var response = await _client.DeleteAsync($"{BaseUrl}/schedule/{scheduleId}");

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            await _factory.SeedAsync(async db =>
            {
                (await db.DoctorSchedules.AnyAsync(s => s.Id == scheduleId)).Should().BeFalse();
                (await db.AppointmentSlots.AnyAsync(s => s.DoctorScheduleId == scheduleId)).Should().BeFalse();
            });
        }

        [Fact]
        public async Task DeleteSchedule_AnotherDoctorsSchedule_Returns404AndKeepsIt()
        {
            Guid otherDoctorUserId = default;
            int scheduleId = 0;
            await _factory.SeedAsync(async db =>
            {
                (_, var ownerId) = await SeedDataHelper.SeedDoctorAsync(db, "owner");
                (otherDoctorUserId, _) = await SeedDataHelper.SeedDoctorAsync(db, "intruder");
                (scheduleId, _) = await SeedDataHelper.SeedScheduleWithSlotsAsync(db, ownerId, DayOfWeek.Monday);
            });
            AuthenticateAs(otherDoctorUserId, "Doctor");

            var response = await _client.DeleteAsync($"{BaseUrl}/schedule/{scheduleId}");

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await response.Content.ReadAsStringAsync()).Should().Contain("Doctor.ScheduleNotFound");
            await _factory.SeedAsync(async db =>
                (await db.DoctorSchedules.AnyAsync(s => s.Id == scheduleId)).Should().BeTrue());
        }

        [Fact]
        public async Task DeleteSchedule_WithAppointmentHistory_Returns500AndKeepsEverything()
        {
            // Documents REVIEW.md H4 and contract #17: the slot→appointment FK is NoAction, so the cascade fails.
            // When H4 is fixed this should become a 4xx (e.g. Doctor.CannotDeleteScheduleWithBookedSlots)
            Guid userId = default;
            int scheduleId = 0, appointmentId = 0;
            await _factory.SeedAsync(async db =>
            {
                (userId, var doctorId) = await SeedDataHelper.SeedDoctorAsync(db, "history");
                (scheduleId, var slotIds) = await SeedDataHelper.SeedScheduleWithSlotsAsync(db, doctorId, DayOfWeek.Monday,
                    (3, new TimeOnly(9, 0), SlotStatus.Booked));
                var patientUserId = await SeedDataHelper.SeedPatientOnlyAsync(db, "history");
                var patientId = db.PatientProfiles.Single(p => p.UserId == patientUserId).Id;
                var appointment = new Appointment
                {
                    PatientProfileId = patientId, DoctorProfileId = doctorId, SlotId = slotIds[0], Status = AppointmentStatus.Confirmed
                };
                db.Appointments.Add(appointment);
                await db.SaveChangesAsync();
                appointmentId = appointment.Id;
            });
            AuthenticateAs(userId, "Doctor");

            var response = await _client.DeleteAsync($"{BaseUrl}/schedule/{scheduleId}");

            response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
            await _factory.SeedAsync(async db =>
            {
                (await db.DoctorSchedules.AnyAsync(s => s.Id == scheduleId)).Should().BeTrue();
                (await db.Appointments.AnyAsync(a => a.Id == appointmentId)).Should().BeTrue();
            });
        }

        // ----------------------------------------------------------------

        private async Task<List<DoctorSummaryResponse>> SearchAsync(string query)
        {
            var response = await _client.GetAsync(BaseUrl + query);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            return (await response.Content.ReadFromJsonAsync<List<DoctorSummaryResponse>>(_json))!;
        }

        private void AuthenticateAs(Guid userId, string role)
        {
            _client.DefaultRequestHeaders.Clear();
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId.ToString());
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserRoleHeader, role);
        }
    }
}
