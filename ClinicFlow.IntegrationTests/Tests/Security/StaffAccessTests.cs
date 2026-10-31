using ClinicFlow.IntegrationTests.Infrastructure;
using Domain.Entities.Staff;
using Domain.Entities.Tenancy;
using Domain.Enums;
using FluentAssertions;
using Shared.Authorization;
using Shared.DTOs.Auth;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;

namespace ClinicFlow.IntegrationTests.Tests.Security
{
    // Staff members and branch-scoped access over HTTP (P1 PR 4; ADR 0002): the claims a real login writes, /me, and
    // the scope checks of the V2 routes once roles are assigned to branches
    [Collection(nameof(IntegrationTestCollection))]
    public class StaffAccessTests(ClinicFlowApiFactory factory) : IAsyncLifetime
    {
        private const string Password = "Test@1234!";
        private readonly HttpClient _client = factory.CreateClient();
        private Guid _doctorUserId;
        private int _otherBranchId;

        public async Task InitializeAsync()
        {
            await factory.ResetDataBaseAsync();
            _client.DefaultRequestHeaders.Clear();

            await factory.SeedAsync(async db =>
            {
                (_doctorUserId, _) = await SeedDataHelper.SeedDoctorAsync(db, "staff");
                var other = db.Branches.Add(new Branch
                {
                    Code = "NSR", NameEn = "Nasr City", NameAr = "مدينة نصر", TimeZoneId = "Africa/Cairo",
                }).Entity;
                await db.SaveChangesAsync();
                _otherBranchId = other.Id;

                // The doctor also manages the organization, and is its medical director
                db.StaffMembers.Add(new StaffMember
                {
                    UserId = _doctorUserId,
                    Status = StaffStatus.Active,
                    IsMedicalDirector = true,
                    Assignments =
                    [
                        new StaffRoleAssignment { Role = Roles.Doctor, BranchId = Branch.MainId },
                        new StaffRoleAssignment { Role = Roles.Manager, BranchId = null },
                    ],
                });
                await db.SaveChangesAsync();
            });
        }

        public Task DisposeAsync() => Task.CompletedTask;

        private void AuthenticateAs(string roles, params string[] roleScopes)
        {
            _client.DefaultRequestHeaders.Clear();
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, _doctorUserId.ToString());
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserRoleHeader, roles);
            foreach (var scope in roleScopes)
                _client.DefaultRequestHeaders.Add(TestAuthHandler.RoleScopeHeader, scope);
        }

        private static JsonObject PayloadOf(string jwt)
        {
            var part = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
            part = part.PadRight(part.Length + (4 - part.Length % 4) % 4, '=');
            return JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(part)))!.AsObject();
        }

        private static IEnumerable<string> ValuesOf(JsonObject payload, string claim)
            => payload[claim] switch
            {
                JsonArray array => array.Select(v => v!.GetValue<string>()),
                JsonValue value => [value.GetValue<string>()],
                _ => [],
            };

        [Fact]
        public async Task Login_AStaffMember_WritesTheRolesAndScopesOfTheAssignments()
        {
            var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest("doctorstaff@test.com", Password));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
            body["role"]!.GetValue<string>().Should().Be(Roles.Manager); // the first in role order (REVIEW.md NL10)

            var payload = PayloadOf(body["accessToken"]!.GetValue<string>());
            ValuesOf(payload, "http://schemas.microsoft.com/ws/2008/06/identity/claims/role").Should().BeEquivalentTo([Roles.Doctor, Roles.Manager]);
            ValuesOf(payload, ClinicClaims.RoleScope).Should().BeEquivalentTo(["Doctor=1", "Manager=*"]);
            ValuesOf(payload, ClinicClaims.Organization).Should().Equal("1");
            ValuesOf(payload, ClinicClaims.StaffMember).Should().ContainSingle();
            ValuesOf(payload, ClinicClaims.MedicalDirector).Should().Equal("true");
        }

        [Fact]
        public async Task Me_ReturnsTheRolesWithTheirScopesAndTheGrantedPermissions()
        {
            AuthenticateAs("Doctor,Manager", "Doctor=1", "Manager=*");

            var response = await _client.GetAsync("/api/v2/me");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var me = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
            me["id"]!.GetValue<string>().Should().Be(_doctorUserId.ToString());
            me["fullName"]!.GetValue<string>().Should().Be("Doctor staff");
            var doctor = me["roles"]!.AsArray().Single(r => r!["role"]!.GetValue<string>() == Roles.Doctor)!;
            doctor["scope"]!.GetValue<string>().Should().Be("Branch");
            doctor["branchIds"]!.AsArray().Select(b => b!.GetValue<string>()).Should().Equal("1");
            me["roles"]!.AsArray().Single(r => r!["role"]!.GetValue<string>() == Roles.Manager)!.AsObject()
                .ContainsKey("branchIds").Should().BeFalse();
            me["permissions"]!.AsArray().Should().Contain(p =>
                p!["permission"]!.GetValue<string>() == Permissions.Organization.SettingsManage && p["scope"]!.GetValue<string>() == "Organization");
        }

        [Fact]
        public async Task Me_Anonymous_Returns401()
            => (await _client.GetAsync("/api/v2/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        [Fact]
        public async Task SetWorkingHours_ABranchManager_OnlyAtTheirBranch()
        {
            AuthenticateAs(Roles.Manager, $"Manager={Branch.MainId}");

            var own = await _client.PutAsJsonAsync($"/api/v2/branches/{Branch.MainId}/working-hours", Array.Empty<object>());
            var other = await _client.PutAsJsonAsync($"/api/v2/branches/{_otherBranchId}/working-hours", Array.Empty<object>());

            own.StatusCode.Should().Be(HttpStatusCode.OK);
            other.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await other.Content.ReadFromJsonAsync<JsonObject>())!["title"]!.GetValue<string>().Should().Be("Access.OutOfScope");
        }

        [Fact]
        public async Task PatchSettings_OrganizationWideManager_Returns200_BranchManager_Returns403()
        {
            // org.settings.manage is a Manager's only when assigned organization-wide (02 §6 "org managers only")
            AuthenticateAs(Roles.Manager, "Manager=*");
            (await _client.PatchAsJsonAsync("/api/v2/organization/settings", new { walkInCap = 6 })).StatusCode.Should().Be(HttpStatusCode.OK);

            AuthenticateAs(Roles.Manager, "Manager=1");
            (await _client.PatchAsJsonAsync("/api/v2/organization/settings", new { walkInCap = 7 })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        [Fact]
        public async Task CreateBranch_OrganizationWideManager_Returns201()
        {
            // branches.manage at organization scope: the Manager's Branch grant widened by the assignment
            AuthenticateAs(Roles.Manager, "Manager=*");

            var response = await _client.PostAsJsonAsync("/api/v2/branches", new
            {
                code = "HLP", nameEn = "Heliopolis", nameAr = "مصر الجديدة", addressEn = "1 Baghdad St", addressAr = "١ شارع بغداد",
                phone = "01001234567", timeZone = "Africa/Cairo",
            });

            response.StatusCode.Should().Be(HttpStatusCode.Created);
        }
    }
}
