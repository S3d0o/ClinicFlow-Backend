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

        // ---- visibility of unapproved and suspended doctors (REVIEW.md M1) ----

        [Theory]
        [InlineData(false, true)]  // not approved yet
        [InlineData(true, false)]  // suspended
        public async Task HiddenDoctor_ProfileReturns404SlotsAreEmptyAndBookingReturns404(bool approved, bool active)
        {
            int doctorId = 0, slotId = 0;
            Guid patientUserId = default;
            await _factory.SeedAsync(async db =>
            {
                (_, doctorId) = await SeedDataHelper.SeedDoctorAsync(db, "hidden", approved: approved, active: active);
                (_, var slotIds) = await SeedDataHelper.SeedScheduleWithSlotsAsync(db, doctorId, DayOfWeek.Monday,
                    (3, new TimeOnly(9, 0), SlotStatus.Available));
                slotId = slotIds[0];
                patientUserId = await SeedDataHelper.SeedPatientOnlyAsync(db, "seeker");
            });
            var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)).ToString("yyyy-MM-dd");

            var profile = await _client.GetAsync($"{BaseUrl}/{doctorId}");
            var slots = await _client.GetFromJsonAsync<List<SlotResponse>>($"{BaseUrl}/{doctorId}/slots?date={date}", _json);
            AuthenticateAs(patientUserId, "Patient");
            var booking = await _client.PostAsJsonAsync("/api/appointments", new { slotId, reasonForVisit = "Checkup" });

            profile.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await profile.Content.ReadAsStringAsync()).Should().Contain("Doctor.NotFound");
            slots.Should().BeEmpty();
            booking.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await booking.Content.ReadAsStringAsync()).Should().Contain("Appointment.SlotNotFound");
            await _factory.SeedAsync(async db =>
                (await db.AppointmentSlots.AsNoTracking().SingleAsync(s => s.Id == slotId)).Status.Should().Be(SlotStatus.Available));
        }

        [Fact]
        public async Task ApprovedDoctor_ProfileReturns200WithSpecialty()
        {
            int doctorId = 0;
            await _factory.SeedAsync(async db => (_, doctorId) = await SeedDataHelper.SeedDoctorAsync(db, "public", specialtyName: "Cardiology"));

            var profile = await _client.GetFromJsonAsync<DoctorResponse>($"{BaseUrl}/{doctorId}", _json);

            profile!.Id.Should().Be(doctorId);
            profile.SpecialtyName.Should().Be("Cardiology");
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
        public async Task UpdateProfile_WithoutFee_Returns204AndKeepsTheFee()
        {
            // REVIEW.md M15: the fee is optional, and omitting it keeps the current value
            Guid userId = default;
            await _factory.SeedAsync(async db => (userId, _) = await SeedDataHelper.SeedDoctorAsync(db, "nofee", fee: 300));
            AuthenticateAs(userId, "Doctor");

            var response = await _client.PutAsJsonAsync($"{BaseUrl}/profile", new { bio = "Only the bio" });

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            await _factory.SeedAsync(async db =>
            {
                var profile = await db.DoctorProfiles.AsNoTracking().SingleAsync(d => d.UserId == userId);
                profile.ConsultationFee.Should().Be(300m);
                profile.Bio.Should().Be("Only the bio");
            });
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
        public async Task DeleteSchedule_WithUpcomingBooking_Returns409AndKeepsEverything()
        {
            // REVIEW.md H4: this used to fail on the NoAction slot→appointment FK with a 500
            Guid userId = default;
            int scheduleId = 0, appointmentId = 0;
            await _factory.SeedAsync(async db =>
            {
                (userId, var doctorId) = await SeedDataHelper.SeedDoctorAsync(db, "upcoming");
                (scheduleId, var slotIds) = await SeedDataHelper.SeedScheduleWithSlotsAsync(db, doctorId, DayOfWeek.Monday,
                    (3, new TimeOnly(9, 0), SlotStatus.Booked));
                appointmentId = await AddAppointmentAsync(db, doctorId, slotIds[0], AppointmentStatus.Confirmed, "upcoming");
            });
            AuthenticateAs(userId, "Doctor");

            var response = await _client.DeleteAsync($"{BaseUrl}/schedule/{scheduleId}");

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await response.Content.ReadAsStringAsync()).Should().Contain("Doctor.CannotDeleteScheduleWithBookedSlots");
            await _factory.SeedAsync(async db =>
            {
                (await db.DoctorSchedules.AsNoTracking().SingleAsync(s => s.Id == scheduleId)).IsActive.Should().BeTrue();
                (await db.Appointments.AnyAsync(a => a.Id == appointmentId)).Should().BeTrue();
            });
        }

        [Fact]
        public async Task DeleteSchedule_WithOnlyPastAppointments_Returns204DeactivatesItAndTheDayCanBeRecreated()
        {
            // REVIEW.md H4: the past appointment keeps its slot, so the schedule is deactivated rather than deleted
            Guid userId = default;
            int scheduleId = 0, appointmentId = 0, pastSlotId = 0, freeSlotId = 0;
            var day = DateTime.UtcNow.AddDays(3).DayOfWeek;
            await _factory.SeedAsync(async db =>
            {
                (userId, var doctorId) = await SeedDataHelper.SeedDoctorAsync(db, "past");
                (scheduleId, var slotIds) = await SeedDataHelper.SeedScheduleWithSlotsAsync(db, doctorId, day,
                    (-4, new TimeOnly(9, 0), SlotStatus.Booked),
                    (3, new TimeOnly(9, 0), SlotStatus.Available));
                (pastSlotId, freeSlotId) = (slotIds[0], slotIds[1]);
                appointmentId = await AddAppointmentAsync(db, doctorId, pastSlotId, AppointmentStatus.Completed, "past");
            });
            AuthenticateAs(userId, "Doctor");

            var response = await _client.DeleteAsync($"{BaseUrl}/schedule/{scheduleId}");

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            await _factory.SeedAsync(async db =>
            {
                (await db.DoctorSchedules.AsNoTracking().SingleAsync(s => s.Id == scheduleId)).IsActive.Should().BeFalse();
                (await db.Appointments.AnyAsync(a => a.Id == appointmentId)).Should().BeTrue();
                (await db.AppointmentSlots.AnyAsync(s => s.Id == pastSlotId)).Should().BeTrue();
                (await db.AppointmentSlots.AnyAsync(s => s.Id == freeSlotId)).Should().BeFalse();
            });

            // One schedule per weekday: creating that day again reuses the deactivated schedule
            var recreate = await _client.PostAsJsonAsync($"{BaseUrl}/schedule", new CreateScheduleRequest
            {
                DayOfWeek = day, StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(12, 0), SlotDurationMinutes = 30
            });

            recreate.StatusCode.Should().Be(HttpStatusCode.Created);
            (await recreate.Content.ReadFromJsonAsync<ScheduleResponse>(_json))!.Id.Should().Be(scheduleId);
            await _factory.SeedAsync(async db =>
                (await db.DoctorSchedules.AsNoTracking().SingleAsync(s => s.Id == scheduleId)).IsActive.Should().BeTrue());
        }

        [Fact]
        public async Task UpdateSchedule_WithBookedAndCancelledSlots_Returns200AndKeepsThem()
        {
            // REVIEW.md H4: regenerating used to collide with the booked 09:00 slot (unique index) and to fail deleting
            // the 09:30 slot a cancelled appointment still points to (NoAction FK), both as a 500 after a partial save
            Guid userId = default;
            int scheduleId = 0, bookedSlotId = 0, cancelledSlotId = 0, confirmedId = 0;
            var day = DateTime.UtcNow.AddDays(3).DayOfWeek;
            var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3));
            await _factory.SeedAsync(async db =>
            {
                (userId, var doctorId) = await SeedDataHelper.SeedDoctorAsync(db, "update");
                (scheduleId, var slotIds) = await SeedDataHelper.SeedScheduleWithSlotsAsync(db, doctorId, day,
                    (3, new TimeOnly(9, 0), SlotStatus.Booked),
                    (3, new TimeOnly(9, 30), SlotStatus.Available),
                    (3, new TimeOnly(10, 0), SlotStatus.Available));
                (bookedSlotId, cancelledSlotId) = (slotIds[0], slotIds[1]);
                confirmedId = await AddAppointmentAsync(db, doctorId, bookedSlotId, AppointmentStatus.Confirmed, "booked");
                await AddAppointmentAsync(db, doctorId, cancelledSlotId, AppointmentStatus.Cancelled, "cancelled");
            });
            AuthenticateAs(userId, "Doctor");

            var response = await _client.PutAsJsonAsync($"{BaseUrl}/schedule/{scheduleId}", new UpdateScheduleRequest { SlotDurationMinutes = 60 });

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            await _factory.SeedAsync(async db =>
            {
                (await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == confirmedId)).SlotId.Should().Be(bookedSlotId);
                (await db.AppointmentSlots.AnyAsync(s => s.Id == cancelledSlotId)).Should().BeTrue();
                (await db.DoctorSchedules.AsNoTracking().SingleAsync(s => s.Id == scheduleId)).SlotDurationMinutes.Should().Be(60);

                // New 60-minute slots fill the rest of the day without overlapping the kept 09:00 and 09:30 slots
                var dayslots = await db.AppointmentSlots.AsNoTracking().Where(s => s.DoctorScheduleId == scheduleId && s.Date == date)
                    .OrderBy(s => s.StartTime).ToListAsync();
                dayslots.Select(s => s.StartTime).Should().Equal(
                    new TimeOnly(9, 0), new TimeOnly(9, 30), new TimeOnly(10, 0), new TimeOnly(11, 0), new TimeOnly(12, 0),
                    new TimeOnly(13, 0), new TimeOnly(14, 0), new TimeOnly(15, 0), new TimeOnly(16, 0));
                dayslots.Where(s => s.StartTime >= new TimeOnly(10, 0)).Should().OnlyContain(s => s.EndTime == s.StartTime.AddMinutes(60));
            });
        }

        private static async Task<int> AddAppointmentAsync(Persistence.AppData.ClinicDbContext db, int doctorId, int slotId,
            AppointmentStatus status, string patientSuffix)
        {
            var patientUserId = await SeedDataHelper.SeedPatientOnlyAsync(db, patientSuffix);
            var patientId = db.PatientProfiles.Single(p => p.UserId == patientUserId).Id;
            var appointment = new Appointment { PatientProfileId = patientId, DoctorProfileId = doctorId, SlotId = slotId, Status = status };
            db.Appointments.Add(appointment);
            await db.SaveChangesAsync();
            return appointment.Id;
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
