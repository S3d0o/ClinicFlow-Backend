using ClinicFlow.Domain.Enums;
using ClinicFlow.IntegrationTests.Infrastructure;
using Domain.Entities.AppModule;
using Domain.Entities.IdentityModule;
using Domain.Entities.Tenancy;
using Domain.Enums;
using Domain.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Persistence.AppData;
using Shared.DTOs.Appointment;
using Shared.Enums;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ClinicFlow.IntegrationTests.Tests.Tenancy
{
    // A second organization with a full set of rows (doctor, schedule, slots, appointment, review, notification) must be
    // invisible to the app, which serves organization 1, and the save interceptor must keep each context to its own
    // organization (docs/v2/adr/0001). Users and patient profiles are global, so the same patient appears in both
    [Collection(nameof(IntegrationTestCollection))]
    public class TenancyIsolationTests : IAsyncLifetime
    {
        private sealed class FixedTenant(int organizationId) : ITenantContext
        {
            public int OrganizationId => organizationId;
            public int DefaultBranchId => 0;
        }

        private Guid _patientUserId;
        private int _ownAppointmentId;
        private int _otherOrganizationId;
        private int _otherDoctorId;
        private int _otherSlotId;
        private int _otherAppointmentId;
        private int _otherReviewId;
        private int _otherNotificationId;

        private readonly ClinicFlowApiFactory _factory;
        private readonly HttpClient _client;

        public TenancyIsolationTests(ClinicFlowApiFactory factory)
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
                (_patientUserId, _, _, _ownAppointmentId) = await SeedDataHelper.SeedBaseAppointmentAsync(db);

                // Organization is global, so the app's context may create the second one
                var other = new Organization { LegalName = "Other Clinic", BrandName = "Other", DefaultTimeZoneId = "Africa/Cairo" };
                db.Organizations.Add(other);
                await db.SaveChangesAsync();
                _otherOrganizationId = other.Id;
            });

            await using var otherDb = OtherOrganizationContext();
            otherDb.RecordAudit = false; // seeded history, as in SeedAsync
            await SeedOtherOrganizationAsync(otherDb);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        private ClinicDbContext OtherOrganizationContext() => new(
            new DbContextOptionsBuilder<ClinicDbContext>().UseSqlServer(_factory.TestConnectionString).Options,
            new FixedTenant(_otherOrganizationId));

        private async Task SeedOtherOrganizationAsync(ClinicDbContext db)
        {
            var branch = new Branch { Code = Branch.MainCode, NameAr = "فرع", NameEn = "Other main", TimeZoneId = "Africa/Cairo" };
            db.Branches.Add(branch);

            var doctorUser = new ApplicationUser
            {
                Id = Guid.NewGuid(), FirstName = "Other", LastName = "Doctor",
                UserName = "doctor@other.test", NormalizedUserName = "DOCTOR@OTHER.TEST",
                Email = "doctor@other.test", NormalizedEmail = "DOCTOR@OTHER.TEST",
                EmailConfirmed = true, IsActive = true, Gender = Gender.Male, SecurityStamp = Guid.NewGuid().ToString(),
            };
            doctorUser.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(doctorUser, "Test@1234!");
            db.Users.Add(doctorUser);

            var doctor = new DoctorProfile
            {
                UserId = doctorUser.Id,
                SpecialtyId = (await db.Specialties.FirstAsync()).Id, // specialties are global
                ConsultationFee = 300,
                IsApprovedByAdmin = true,
            };
            db.DoctorProfiles.Add(doctor);
            await db.SaveChangesAsync();

            var today = _factory.ClinicToday;
            var schedule = new DoctorSchedule
            {
                DoctorProfileId = doctor.Id, BranchId = branch.Id, DayOfWeek = today.AddDays(2).DayOfWeek,
                StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(17, 0), SlotDurationMinutes = 30,
            };
            db.DoctorSchedules.Add(schedule);
            await db.SaveChangesAsync();

            var freeSlot = Slot(today.AddDays(2), new TimeOnly(9, 0), SlotStatus.Available);
            var pastSlot = Slot(today.AddDays(-5), new TimeOnly(9, 0), SlotStatus.Booked);
            db.AppointmentSlots.AddRange(freeSlot, pastSlot);
            await db.SaveChangesAsync();

            // The org-1 patient (patient profiles are global) also visited the other clinic
            var patientProfileId = (await db.PatientProfiles.SingleAsync(p => p.UserId == _patientUserId)).Id;
            var appointment = new Appointment
            {
                SlotId = pastSlot.Id, BranchId = branch.Id, DoctorProfileId = doctor.Id, PatientProfileId = patientProfileId,
                BookedAt = DateTime.UtcNow.AddDays(-10),
            };
            db.Appointments.Add(appointment).Property(a => a.Status).CurrentValue = AppointmentStatus.Completed;
            await db.SaveChangesAsync();

            var review = new Review
            {
                AppointmentId = appointment.Id, DoctorProfileId = doctor.Id, PatientProfileId = patientProfileId,
                Rating = 5, Comment = "Other clinic", CreatedAt = DateTime.UtcNow,
            };
            var notification = new Notification
            {
                UserId = _patientUserId, Title = "Other clinic", Message = "From organization 2",
                Type = NotificationType.SystemAlert, CreatedAt = DateTime.UtcNow,
            };
            db.AddRange(review, notification);
            await db.SaveChangesAsync();

            (_otherDoctorId, _otherSlotId, _otherAppointmentId, _otherReviewId, _otherNotificationId) =
                (doctor.Id, freeSlot.Id, appointment.Id, review.Id, notification.Id);

            AppointmentSlot Slot(DateOnly date, TimeOnly start, SlotStatus status) => new()
            {
                DoctorProfileId = doctor.Id, DoctorScheduleId = schedule.Id, BranchId = branch.Id,
                Date = date, StartTime = start, EndTime = start.AddMinutes(30), Status = status,
                StartsAtUtc = SeedDataHelper.SlotUtc(date, start), EndsAtUtc = SeedDataHelper.SlotUtc(date, start.AddMinutes(30)),
            };
        }

        // ── Reads through the API (organization 1) ───────────────────────────

        [Fact]
        public async Task GetDoctors_DoesNotListAnotherOrganizationsDoctor()
        {
            var ids = await GetIdsAsync("/api/doctors");

            ids.Should().NotBeEmpty().And.NotContain(_otherDoctorId);
        }

        [Fact]
        public async Task GetDoctor_FromAnotherOrganization_Returns404()
            => (await _client.GetAsync($"/api/doctors/{_otherDoctorId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        [Fact]
        public async Task GetDoctorSlots_FromAnotherOrganization_ReturnsNone()
        {
            var date = _factory.ClinicToday.AddDays(2).ToString("yyyy-MM-dd");

            (await GetIdsAsync($"/api/doctors/{_otherDoctorId}/slots?date={date}")).Should().BeEmpty();
        }

        [Fact]
        public async Task GetDoctorReviews_FromAnotherOrganization_ReturnsNone()
        {
            (await GetIdsAsync($"/api/doctors/{_otherDoctorId}/reviews")).Should().NotContain(_otherReviewId);

            var legacy = await _client.GetAsync($"/api/reviews/doctor/{_otherDoctorId}");
            if (legacy.StatusCode == HttpStatusCode.OK)
                (await IdsAsync(legacy)).Should().NotContain(_otherReviewId);
            else
                legacy.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task GetMyAppointments_ShowsOnlyThisOrganizationsVisits()
        {
            AuthenticateAsPatient();

            (await GetIdsAsync("/api/appointments/my")).Should().Equal(_ownAppointmentId);
        }

        [Fact]
        public async Task GetAppointment_FromAnotherOrganization_Returns404()
        {
            AuthenticateAsPatient();

            (await _client.GetAsync($"/api/appointments/{_otherAppointmentId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task BookAppointment_SlotOfAnotherOrganization_Returns404AndBooksNothing()
        {
            AuthenticateAsPatient();

            var response = await _client.PostAsJsonAsync("/api/appointments", new BookAppointmentRequest(_otherSlotId, null));

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            await using var otherDb = OtherOrganizationContext();
            (await otherDb.AppointmentSlots.SingleAsync(s => s.Id == _otherSlotId)).Status.Should().Be(SlotStatus.Available);
        }

        [Fact]
        public async Task GetMyNotifications_LeavesOutAnotherOrganizationsNotifications()
        {
            AuthenticateAsPatient();

            (await GetIdsAsync("/api/notifications/my")).Should().NotContain(_otherNotificationId);
        }

        // ── The filters and the save interceptor ────────────────────────────

        [Fact]
        public async Task EachContext_SeesOnlyItsOwnOrganizationsRows()
        {
            await using var otherDb = OtherOrganizationContext();
            (await otherDb.Appointments.Select(a => a.Id).ToListAsync()).Should().Equal(_otherAppointmentId);

            await _factory.SeedAsync(async db =>
            {
                (await db.Appointments.Select(a => a.Id).ToListAsync()).Should().Equal(_ownAppointmentId);
                (await db.Appointments.IgnoreQueryFilters().CountAsync()).Should().Be(2);
            });
        }

        [Fact]
        public async Task SaveChanges_NewRowWithoutOrganization_GetsTheContextsOrganization()
        {
            var id = 0;
            await _factory.SeedAsync(async db =>
            {
                var notification = new Notification
                {
                    UserId = _patientUserId, Title = "Stamped", Message = "No organization set", CreatedAt = DateTime.UtcNow,
                };
                db.Notifications.Add(notification);
                await db.SaveChangesAsync();
                id = notification.Id;
            });

            await _factory.SeedAsync(async db =>
                (await db.Notifications.SingleAsync(n => n.Id == id)).OrganizationId.Should().Be(Organization.DefaultId));
        }

        [Fact]
        public async Task SaveChanges_RowOfAnotherOrganization_Throws()
        {
            await _factory.SeedAsync(async db =>
            {
                db.Notifications.Add(new Notification
                {
                    OrganizationId = _otherOrganizationId, UserId = _patientUserId, Title = "Wrong", Message = "Other organization",
                    CreatedAt = DateTime.UtcNow,
                });

                var act = () => db.SaveChangesAsync();
                await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*organization*");
            });
        }

        [Fact]
        public async Task SaveChanges_MovingARowToAnotherOrganization_Throws()
        {
            await _factory.SeedAsync(async db =>
            {
                var appointment = await db.Appointments.SingleAsync(a => a.Id == _ownAppointmentId);
                appointment.OrganizationId = _otherOrganizationId;

                var act = () => db.SaveChangesAsync();
                await act.Should().ThrowAsync<InvalidOperationException>();
            });
        }

        private void AuthenticateAsPatient()
        {
            _client.DefaultRequestHeaders.Clear();
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, _patientUserId.ToString());
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserRoleHeader, "Patient");
        }

        private async Task<List<int>> GetIdsAsync(string url)
        {
            var response = await _client.GetAsync(url);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            return await IdsAsync(response);
        }

        private static async Task<List<int>> IdsAsync(HttpResponseMessage response)
        {
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return body.RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetInt32()).ToList();
        }
    }
}
