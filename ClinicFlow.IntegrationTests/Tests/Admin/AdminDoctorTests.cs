using ClinicFlow.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Shared.DTOs.Auth;
using Shared.DTOs.Doctor;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClinicFlow.IntegrationTests.Tests.Admin
{
    [Collection(nameof(IntegrationTestCollection))]
    public class AdminDoctorTests : IAsyncLifetime
    {
        private const string AdminUrl = "/api/admin/doctors";
        private const string LoginUrl = "/api/auth/login";
        private const string RefreshUrl = "/api/auth/refresh-token";

        // Seeded by SeedDataHelper.SeedBaseAppointmentAsync
        private const string DoctorEmail = "doctor@test.com";
        private const string DoctorPassword = "Test@1234!";

        private Guid _doctorUserId;
        private int _doctorProfileId;
        private readonly Guid _adminUserId = Guid.NewGuid();

        private readonly ClinicFlowApiFactory _factory;
        private readonly HttpClient _client;
        private readonly JsonSerializerOptions _json;

        public AdminDoctorTests(ClinicFlowApiFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
            _json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            _json.Converters.Add(new JsonStringEnumConverter());
        }

        public Task DisposeAsync() => Task.CompletedTask;

        public async Task InitializeAsync()
        {
            await _factory.ResetDataBaseAsync();
            _client.DefaultRequestHeaders.Clear();

            await _factory.SeedAsync(async db =>
            {
                (_, _doctorUserId, _, _) = await SeedDataHelper.SeedBaseAppointmentAsync(db);
                _doctorProfileId = (await db.DoctorProfiles.SingleAsync(d => d.UserId == _doctorUserId)).Id;
            });
        }

        [Fact]
        public async Task SuspendDoctor_ActiveDoctor_Returns204AndRevokesRefreshTokens()
        {
            // Arrange — the doctor has a live session
            var login = await LoginAsDoctorAsync();
            AuthenticateAs(_adminUserId, "Admin");

            // Act
            var response = await _client.PostAsync($"{AdminUrl}/{_doctorProfileId}/suspend", null);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            await _factory.SeedAsync(async db =>
            {
                var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == _doctorUserId);
                user.IsActive.Should().BeFalse();

                var tokens = await db.RefreshTokens.AsNoTracking().Where(t => t.UserId == _doctorUserId).ToListAsync();
                tokens.Should().NotBeEmpty()
                    .And.OnlyContain(t => t.Revoked != null && t.RevocationReason == "Account suspended");
            });

            _client.DefaultRequestHeaders.Clear();
            var refresh = await _client.PostAsJsonAsync(RefreshUrl, new RefreshTokenRequest(login.RefreshToken));
            refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            var relogin = await _client.PostAsJsonAsync(LoginUrl, new LoginRequest(DoctorEmail, DoctorPassword));
            relogin.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task SuspendDoctor_AlreadySuspended_Returns409WithAlreadySuspendedCode()
        {
            AuthenticateAs(_adminUserId, "Admin");
            (await _client.PostAsync($"{AdminUrl}/{_doctorProfileId}/suspend", null))
                .StatusCode.Should().Be(HttpStatusCode.NoContent);

            var response = await _client.PostAsync($"{AdminUrl}/{_doctorProfileId}/suspend", null);

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await response.Content.ReadAsStringAsync()).Should().Contain("Admin.DoctorAlreadySuspended");
        }

        [Fact]
        public async Task SuspendDoctor_UnknownDoctor_Returns404()
        {
            AuthenticateAs(_adminUserId, "Admin");

            var response = await _client.PostAsync($"{AdminUrl}/99999/suspend", null);

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Theory]
        [InlineData("Doctor")]
        [InlineData("Patient")]
        public async Task SuspendDoctor_NonAdmin_Returns403AndKeepsDoctorActive(string role)
        {
            AuthenticateAs(_doctorUserId, role);

            var response = await _client.PostAsync($"{AdminUrl}/{_doctorProfileId}/suspend", null);

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            await _factory.SeedAsync(async db =>
                (await db.Users.AsNoTracking().SingleAsync(u => u.Id == _doctorUserId)).IsActive.Should().BeTrue());
        }

        [Fact]
        public async Task ApproveDoctor_PendingDoctor_Returns204AndRemovesFromPendingList()
        {
            // Arrange
            await SetDoctorApprovedAsync(false);
            AuthenticateAs(_adminUserId, "Admin");
            (await GetPendingAsync()).Should().ContainSingle(d => d.Id == _doctorProfileId);

            // Act
            var response = await _client.PostAsync($"{AdminUrl}/{_doctorProfileId}/approve", null);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await GetPendingAsync()).Should().BeEmpty();
            await _factory.SeedAsync(async db =>
                (await db.DoctorProfiles.AsNoTracking().SingleAsync(d => d.Id == _doctorProfileId))
                    .IsApprovedByAdmin.Should().BeTrue());
        }

        [Fact]
        public async Task ApproveDoctor_AlreadyApproved_Returns409WithAlreadyApprovedCode()
        {
            AuthenticateAs(_adminUserId, "Admin");

            var response = await _client.PostAsync($"{AdminUrl}/{_doctorProfileId}/approve", null);

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await response.Content.ReadAsStringAsync()).Should().Contain("Admin.DoctorAlreadyApproved");
        }

        [Fact]
        public async Task GetPendingDoctors_SuspendedPendingDoctor_IsNotListed()
        {
            await SetDoctorApprovedAsync(false);
            AuthenticateAs(_adminUserId, "Admin");
            (await _client.PostAsync($"{AdminUrl}/{_doctorProfileId}/suspend", null))
                .StatusCode.Should().Be(HttpStatusCode.NoContent);

            (await GetPendingAsync()).Should().BeEmpty();

            // REVIEW.md NL23: the dashboard count used to include the suspended applicant
            var overview = await _client.GetFromJsonAsync<JsonElement>("/api/admin/stats/overview");
            overview.GetProperty("pendingApprovals").GetInt32().Should().Be(0);
        }

        [Fact]
        public async Task GetPendingDoctors_WithPageSize_ReturnsOnePageAndTheTotalInTheHeader()
        {
            // REVIEW.md NL2: three applicants, oldest first, two per page
            await SetDoctorApprovedAsync(false);
            await _factory.SeedAsync(async db =>
            {
                await SeedDataHelper.SeedDoctorAsync(db, "pending2", approved: false);
                await SeedDataHelper.SeedDoctorAsync(db, "pending3", approved: false);
            });
            AuthenticateAs(_adminUserId, "Admin");

            var all = await _client.GetAsync($"{AdminUrl}/pending");
            var secondPage = await _client.GetAsync($"{AdminUrl}/pending?pageNumber=2&pageSize=2");

            all.Headers.GetValues("X-Total-Count").Should().Equal("3");
            (await all.Content.ReadFromJsonAsync<List<DoctorResponse>>(_json)).Should().HaveCount(3);
            secondPage.Headers.GetValues("X-Total-Count").Should().Equal("3");
            (await secondPage.Content.ReadFromJsonAsync<List<DoctorResponse>>(_json)).Should().ContainSingle();
        }

        [Fact]
        public async Task GetPendingDoctors_PageSizeOutOfRange_Returns400()
        {
            AuthenticateAs(_adminUserId, "Admin");

            var response = await _client.GetAsync($"{AdminUrl}/pending?pageSize=101");

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        // ----------------------------------------------------------------

        private async Task<LoginResponse> LoginAsDoctorAsync()
        {
            _client.DefaultRequestHeaders.Clear();
            var response = await _client.PostAsJsonAsync(LoginUrl, new LoginRequest(DoctorEmail, DoctorPassword));
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            return (await response.Content.ReadFromJsonAsync<LoginResponse>(_json))!;
        }

        private async Task<List<DoctorResponse>> GetPendingAsync()
        {
            var response = await _client.GetAsync($"{AdminUrl}/pending");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            return (await response.Content.ReadFromJsonAsync<List<DoctorResponse>>(_json))!;
        }

        private Task SetDoctorApprovedAsync(bool approved) => _factory.SeedAsync(async db =>
        {
            var doctor = await db.DoctorProfiles.SingleAsync(d => d.Id == _doctorProfileId);
            doctor.IsApprovedByAdmin = approved;
            await db.SaveChangesAsync();
        });

        private void AuthenticateAs(Guid userId, string role)
        {
            _client.DefaultRequestHeaders.Clear();
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId.ToString());
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserRoleHeader, role);
        }
    }
}
