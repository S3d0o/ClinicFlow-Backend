using ClinicFlow.IntegrationTests.Infrastructure;
using Domain.Entities.AppModule;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Shared.DTOs.Auth;
using Shared.Enums;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClinicFlow.IntegrationTests.Tests.Auth
{
    [Collection(nameof(IntegrationTestCollection))]
    public class RegistrationTests : IAsyncLifetime
    {
        private const string RegisterPatientUrl = "/api/auth/register/patient";
        private const string RegisterDoctorUrl = "/api/auth/register/doctor";
        private const string ConfirmEmailUrl = "/api/auth/confirm-email";
        private const string LoginUrl = "/api/auth/login";
        private const string Password = "Register@1234";

        private int _specialtyId;

        private readonly ClinicFlowApiFactory _factory;
        private readonly HttpClient _client;
        private readonly JsonSerializerOptions _json;

        public RegistrationTests(ClinicFlowApiFactory factory)
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
            _factory.Emails.Clear();

            await _factory.SeedAsync(async db =>
            {
                await SeedDataHelper.SeedRolesAsync(db);
                var specialty = new Specialty { Name = "Cardiology", IsActive = true };
                db.Specialties.Add(specialty);
                await db.SaveChangesAsync();
                _specialtyId = specialty.Id;
            });
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task RegisterPatient_NewEmail_Returns200AndCreatesUnconfirmedPatient()
        {
            var response = await _client.PostAsJsonAsync(RegisterPatientUrl, PatientRequest("new.patient@test.com"));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response.Content.ReadFromJsonAsync<RegisterResponse>(_json))!.Email.Should().Be("new.patient@test.com");

            await _factory.SeedAsync(async db =>
            {
                var user = await db.Users.AsNoTracking().SingleAsync(u => u.Email == "new.patient@test.com");
                user.EmailConfirmed.Should().BeFalse();
                user.IsActive.Should().BeTrue();
                (await RolesOfAsync(db, user.Id)).Should().Equal("Patient");
                (await db.PatientProfiles.AnyAsync(p => p.UserId == user.Id)).Should().BeTrue();

                _factory.Emails.Sent.Should().ContainSingle(e =>
                    e.Kind == SentEmailKind.EmailConfirmation && e.To == "new.patient@test.com" && e.UserId == user.Id);
            });
        }

        [Fact]
        public async Task RegisterPatient_ConfirmEmailWithTokenFromEmail_AllowsLogin()
        {
            (await _client.PostAsJsonAsync(RegisterPatientUrl, PatientRequest("confirm.me@test.com")))
                .StatusCode.Should().Be(HttpStatusCode.OK);

            var beforeConfirm = await _client.PostAsJsonAsync(LoginUrl, new LoginRequest("confirm.me@test.com", Password));
            beforeConfirm.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await beforeConfirm.Content.ReadAsStringAsync()).Should().Contain("Auth.EmailNotConfirmed");

            // The frontend posts userId and token from the link query string as they are
            var email = _factory.Emails.Sent.Single(e => e.Kind == SentEmailKind.EmailConfirmation);
            var confirm = await _client.PostAsJsonAsync(ConfirmEmailUrl, new ConfirmEmailRequest(email.UserId!.Value.ToString(), email.Token!));
            confirm.StatusCode.Should().Be(HttpStatusCode.NoContent);

            var login = await _client.PostAsJsonAsync(LoginUrl, new LoginRequest("confirm.me@test.com", Password));
            login.StatusCode.Should().Be(HttpStatusCode.OK);
            (await login.Content.ReadFromJsonAsync<LoginResponse>(_json))!.Role.Should().Be("Patient");
        }

        [Fact]
        public async Task ConfirmEmail_InvalidToken_Returns500AndLeavesEmailUnconfirmed()
        {
            (await _client.PostAsJsonAsync(RegisterPatientUrl, PatientRequest("bad.token@test.com")))
                .StatusCode.Should().Be(HttpStatusCode.OK);
            var userId = _factory.Emails.Sent.Single().UserId!.Value;

            var response = await _client.PostAsJsonAsync(ConfirmEmailUrl, new ConfirmEmailRequest(userId.ToString(), "not-a-real-token"));

            // 500 is the documented status today (REVIEW.md M3)
            response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
            (await response.Content.ReadAsStringAsync()).Should().Contain("Auth.InvalidEmailConfirmationToken");
            await _factory.SeedAsync(async db =>
                (await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId)).EmailConfirmed.Should().BeFalse());
        }

        [Fact]
        public async Task ConfirmEmail_UnknownUser_Returns404()
        {
            var response = await _client.PostAsJsonAsync(ConfirmEmailUrl, new ConfirmEmailRequest(Guid.NewGuid().ToString(), "token"));

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await response.Content.ReadAsStringAsync()).Should().Contain("Auth.UserNotFound");
        }

        [Fact]
        public async Task RegisterPatient_DuplicateEmail_Returns500AndKeepsOneAccount()
        {
            (await _client.PostAsJsonAsync(RegisterPatientUrl, PatientRequest("taken@test.com")))
                .StatusCode.Should().Be(HttpStatusCode.OK);

            var response = await _client.PostAsJsonAsync(RegisterPatientUrl, PatientRequest("taken@test.com"));

            // 500 is the documented status today (REVIEW.md M3)
            response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
            (await response.Content.ReadAsStringAsync()).Should().Contain("Auth.EmailAlreadyExists");
            await _factory.SeedAsync(async db =>
                (await db.Users.CountAsync(u => u.Email == "taken@test.com")).Should().Be(1));
        }

        [Fact]
        public async Task RegisterDoctor_ValidRequest_Returns200AndCreatesUnapprovedDoctor()
        {
            var response = await _client.PostAsJsonAsync(RegisterDoctorUrl, DoctorRequest("new.doctor@test.com", _specialtyId));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            await _factory.SeedAsync(async db =>
            {
                var user = await db.Users.AsNoTracking().SingleAsync(u => u.Email == "new.doctor@test.com");
                user.EmailConfirmed.Should().BeFalse();
                (await RolesOfAsync(db, user.Id)).Should().Equal("Doctor");

                var profile = await db.DoctorProfiles.AsNoTracking().SingleAsync(d => d.UserId == user.Id);
                profile.IsApprovedByAdmin.Should().BeFalse();
                profile.SpecialtyId.Should().Be(_specialtyId);
                profile.ConsultationFee.Should().Be(450m);
            });
            _factory.Emails.Sent.Should().ContainSingle(e => e.Kind == SentEmailKind.EmailConfirmation && e.To == "new.doctor@test.com");
        }

        [Fact]
        public async Task RegisterDoctor_UnknownSpecialty_Returns500AndRollsBackTheUser()
        {
            // The profile insert fails on the foreign key after Identity has created the user and
            // assigned the role; the transaction must undo both
            var response = await _client.PostAsJsonAsync(RegisterDoctorUrl, DoctorRequest("orphan@test.com", specialtyId: 999_999));

            response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
            (await response.Content.ReadAsStringAsync()).Should().Contain("Auth.RegistrationFailed");
            await _factory.SeedAsync(async db =>
            {
                (await db.Users.AnyAsync(u => u.Email == "orphan@test.com")).Should().BeFalse();
                (await db.UserRoles.AnyAsync()).Should().BeFalse();
                (await db.DoctorProfiles.AnyAsync()).Should().BeFalse();
            });
            _factory.Emails.Sent.Should().BeEmpty();

            // Nothing is left behind, so the same email can register once the input is fixed
            (await _client.PostAsJsonAsync(RegisterDoctorUrl, DoctorRequest("orphan@test.com", _specialtyId)))
                .StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // ----------------------------------------------------------------

        private static RegisterPatientRequest PatientRequest(string email) => new(
            "Mona", "Adel", email, Password, "+201001234567", Gender.Female, null,
            null, null, null, null, null);

        private static RegisterDoctorRequest DoctorRequest(string email, int specialtyId) => new(
            "Omar", "Fathy", email, Password, "+201009876543", Gender.Male, null,
            specialtyId, "Cardiologist", 12, 450m, "12 Tahrir St", "Cairo");

        private static Task<List<string?>> RolesOfAsync(Persistence.AppData.ClinicDbContext db, Guid userId) =>
            db.UserRoles.Where(ur => ur.UserId == userId)
                .Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name)
                .ToListAsync();
    }
}
