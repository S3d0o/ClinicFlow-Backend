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
using Xunit.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClinicFlow.IntegrationTests.Tests.Appointments
{
    [Collection(nameof(IntegrationTestCollection))]
    public class BookAppointmentTests : IAsyncLifetime
    {
        private const string BaseUrl = "/api/appointments";

        // stable IDs — safe because ResetDatabaseAsync drops+recreates schema
        // so identity columns always restart from 1
        private const int PatientProfileId = 1;
        private const int DoctorProfileId = 1;
        private const int SlotId = 1;

        private Guid _patientUserId;

        private readonly ClinicFlowApiFactory _factory;
        private readonly HttpClient _client;
        private readonly ITestOutputHelper _output;

        public BookAppointmentTests(ClinicFlowApiFactory factory, ITestOutputHelper output)
        {
            _factory = factory;
            _client = factory.CreateClient();
            _output = output;
        }

        public async Task InitializeAsync()
        {
            await _factory.ResetDataBaseAsync();

            _client.DefaultRequestHeaders.Clear();

            await _factory.SeedAsync(async db =>
            {
                var hasher = new PasswordHasher<ApplicationUser>();

                // 1. Specialty (required by DoctorProfile)
                var specialty = new Specialty
                {
                    Name = "General Practice",
                    IsActive = true
                };
                db.Specialties.Add(specialty);
                await db.SaveChangesAsync();

                // 2. Doctor ApplicationUser
                var doctorUser = new ApplicationUser
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Test",
                    LastName = "Doctor",
                    UserName = "doctor@test.com",
                    NormalizedUserName = "DOCTOR@TEST.COM",
                    Email = "doctor@test.com",
                    NormalizedEmail = "DOCTOR@TEST.COM",
                    EmailConfirmed = true,
                    Gender = Gender.Male,
                    SecurityStamp = Guid.NewGuid().ToString(),
                    IsActive = true
                };
                doctorUser.PasswordHash = hasher.HashPassword(doctorUser, "Test@1234!");
                db.Users.Add(doctorUser);
                await db.SaveChangesAsync();

                // 3. DoctorProfile (Id = 1)
                var doctorProfile = new DoctorProfile
                {
                    UserId = doctorUser.Id,
                    SpecialtyId = specialty.Id,
                    YearsOfExperience = 5,
                    ConsultationFee = 200,
                    IsApprovedByAdmin = true
                };
                db.DoctorProfiles.Add(doctorProfile);
                await db.SaveChangesAsync();

                // 4. DoctorSchedule (required FK on AppointmentSlot)
                var schedule = new DoctorSchedule
                {
                    DoctorProfileId = doctorProfile.Id,
                    DayOfWeek = DayOfWeek.Monday,
                    StartTime = new TimeOnly(9, 0),
                    EndTime = new TimeOnly(17, 0),
                    SlotDurationMinutes = 30,
                    IsActive = true
                };
                db.DoctorSchedules.Add(schedule);
                await db.SaveChangesAsync();

                // 5. Patient ApplicationUser
                var patientUser = new ApplicationUser
                {
                    Id = Guid.NewGuid(),
                    FirstName = "Test",
                    LastName = "Patient",
                    UserName = "patient@test.com",
                    NormalizedUserName = "PATIENT@TEST.COM",
                    Email = "patient@test.com",
                    NormalizedEmail = "PATIENT@TEST.COM",
                    EmailConfirmed = true,
                    Gender = Gender.Female,
                    SecurityStamp = Guid.NewGuid().ToString(),
                    IsActive = true
                };
                _patientUserId = patientUser.Id;
                patientUser.PasswordHash = hasher.HashPassword(patientUser, "Test@1234!");
                db.Users.Add(patientUser);
                await db.SaveChangesAsync();

                // 6. PatientProfile (Id = 1)
                var patientProfile = new PatientProfile
                {
                    UserId = patientUser.Id
                };
                db.PatientProfiles.Add(patientProfile);
                await db.SaveChangesAsync();

                // 7. AppointmentSlot (Id = 1)
                var slot = new AppointmentSlot
                {
                    DoctorProfileId = doctorProfile.Id,
                    DoctorScheduleId = schedule.Id,
                    Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)),
                    StartTime = new TimeOnly(10, 0),
                    EndTime = new TimeOnly(10, 30),
                    Status = SlotStatus.Available
                };
                db.AppointmentSlots.Add(slot);
                await db.SaveChangesAsync();
            });
        }

        public Task DisposeAsync() => Task.CompletedTask;

        // ----------------------------------------------------------------

        [Fact]
        public async Task BookAppointment_ValidRequest_Returns201WithAppointmentDetails()
        {
            // Arrange
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, _patientUserId.ToString());
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserRoleHeader, "Patient");

            var request = new BookAppointmentRequest(SlotId, "First visit");

            // Act
            var response = await _client.PostAsJsonAsync($"{BaseUrl}", request);
            _output.WriteLine($"Response: {response.StatusCode}, Content: {await response.Content.ReadAsStringAsync()}");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Created);

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            options.Converters.Add(new JsonStringEnumConverter());

            var body = await response.Content.ReadFromJsonAsync<AppointmentResponse>(options);

            body.Should().NotBeNull();
            body!.SlotId.Should().Be(SlotId);
            body.PatientProfileId.Should().Be(PatientProfileId);
            body.Status.Should().Be(AppointmentStatus.Confirmed);
        }

        [Fact]
        public async Task BookAppointment_SlotNotFound_Returns404()
        {
            // Arrange
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, _patientUserId.ToString());
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserRoleHeader, "Patient");

            var request = new BookAppointmentRequest(9999, "First visit"); // non-existent slot

            // Act
            var response = await _client.PostAsJsonAsync($"{BaseUrl}", request);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task BookAppointment_Unauthenticated_Returns401()
        {
            // Arrange — no auth headers
            var request = new BookAppointmentRequest(SlotId, "First visit");

            // Act
            var response = await _client.PostAsJsonAsync($"{BaseUrl}", request);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }
}