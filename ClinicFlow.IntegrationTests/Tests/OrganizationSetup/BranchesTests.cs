using ClinicFlow.IntegrationTests.Infrastructure;
using Domain.Entities.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Shared.Authorization;
using Shared.DTOs.Appointment;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace ClinicFlow.IntegrationTests.Tests.OrganizationSetup
{
    // Branches and rooms over HTTP (P1 PR 1; API_CONTRACT.md §13.25): the permissions and scopes, the unique code, the
    // last active branch, the public list, the booking refusal at a deactivated branch, and the V2 conventions
    // (ids as strings, coded errors as shape A, camelCase validation keys)
    [Collection(nameof(IntegrationTestCollection))]
    public class BranchesTests(ClinicFlowApiFactory factory) : IAsyncLifetime
    {
        private const string BranchesUrl = "/api/v2/branches";
        private const string PublicBranchesUrl = "/api/v2/public/branches";

        private readonly HttpClient _client = factory.CreateClient();
        private Guid _patientUserId;
        private int _slotId;

        public async Task InitializeAsync()
        {
            await factory.ResetDataBaseAsync();
            _client.DefaultRequestHeaders.Clear();

            await factory.SeedAsync(async db =>
            {
                (_patientUserId, _, _slotId, _) = await SeedDataHelper.SeedBaseAppointmentAsync(db);
            });
        }

        public Task DisposeAsync() => Task.CompletedTask;

        private void AuthenticateAs(string role, Guid? userId = null)
        {
            _client.DefaultRequestHeaders.Clear();
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, (userId ?? Guid.NewGuid()).ToString());
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserRoleHeader, role);
        }

        private static object ABranch(string code = "NSR") => new
        {
            code,
            nameEn = "Nasr City",
            nameAr = "مدينة نصر",
            addressEn = "12 Makram Ebeid",
            addressAr = "١٢ مكرم عبيد",
            phone = "01001234567",
            timeZone = "Africa/Cairo",
        };

        private async Task<string> CreateBranchAsync(string code = "NSR")
        {
            AuthenticateAs(Roles.Admin);
            var response = await _client.PostAsJsonAsync(BranchesUrl, ABranch(code));
            response.StatusCode.Should().Be(HttpStatusCode.Created);
            return (await response.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<string>();
        }

        private static async Task<string?> TitleOf(HttpResponseMessage response)
            => (await response.Content.ReadFromJsonAsync<JsonObject>())!["title"]?.GetValue<string>();

        // ── Reading ─────────────────────────────────────────────────────────

        [Fact]
        public async Task List_AnyStaff_Returns200WithTheMainBranchAndItsIdAsAString()
        {
            AuthenticateAs(Roles.Doctor);

            var response = await _client.GetAsync(BranchesUrl);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var branch = (await response.Content.ReadFromJsonAsync<JsonArray>())!.Single()!.AsObject();
            branch["id"]!.GetValue<string>().Should().Be(Branch.MainId.ToString());
            branch["code"]!.GetValue<string>().Should().Be(Branch.MainCode);
            branch["isActive"]!.GetValue<bool>().Should().BeTrue();
        }

        [Fact]
        public async Task List_Patient_Returns403()
        {
            AuthenticateAs(Roles.Patient);

            var response = await _client.GetAsync(BranchesUrl);

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        // ── Creating and updating ───────────────────────────────────────────

        [Fact]
        public async Task Create_Admin_Returns201AndTheBranchIsListedAndAudited()
        {
            var id = await CreateBranchAsync();

            AuthenticateAs(Roles.Doctor);
            var codes = (await _client.GetFromJsonAsync<JsonArray>(BranchesUrl))!.Select(b => b!["code"]!.GetValue<string>());
            codes.Should().BeEquivalentTo([Branch.MainCode, "NSR"]);

            var audited = false;
            await factory.SeedAsync(async db => audited = await db.AuditEntries.AnyAsync(e => e.EntityType == "Branch" && e.EntityId == id));
            audited.Should().BeTrue();
        }

        [Fact]
        public async Task Create_DuplicateCode_Returns409CodeTaken()
        {
            AuthenticateAs(Roles.Admin);

            var response = await _client.PostAsJsonAsync(BranchesUrl, ABranch(Branch.MainCode));

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await TitleOf(response)).Should().Be("Branch.CodeTaken");
        }

        [Fact]
        public async Task Create_InvalidRequest_Returns400WithCamelCaseKeys()
        {
            AuthenticateAs(Roles.Admin);

            var response = await _client.PostAsJsonAsync(BranchesUrl, new
            {
                code = "nasr",
                nameEn = "Nasr City",
                nameAr = "مدينة نصر",
                addressEn = "12 Makram Ebeid",
                addressAr = "١٢ مكرم عبيد",
                phone = "12",
                timeZone = "Mars/Olympus",
            });

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var errors = (await response.Content.ReadFromJsonAsync<JsonObject>())!["errors"]!.AsObject();
            errors.Select(e => e.Key).Should().BeEquivalentTo(["code", "phone", "timeZone"]);
        }

        [Fact]
        public async Task Create_Doctor_Returns403()
        {
            AuthenticateAs(Roles.Doctor);

            var response = await _client.PostAsJsonAsync(BranchesUrl, ABranch());

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        [Fact]
        public async Task Create_ManagerHoldingBranchesManageForBranchesOnly_Returns403OutOfScope()
        {
            AuthenticateAs(Roles.Manager);

            var response = await _client.PostAsJsonAsync(BranchesUrl, ABranch());

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await TitleOf(response)).Should().Be("Access.OutOfScope");
        }

        [Fact]
        public async Task Update_ExistingBranch_Returns200WithTheNewValues()
        {
            var id = await CreateBranchAsync();

            var response = await _client.PutAsJsonAsync($"{BranchesUrl}/{id}", ABranch("NCY"));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>().Should().Be("NCY");
        }

        [Fact]
        public async Task Update_UnknownBranch_Returns404()
        {
            AuthenticateAs(Roles.Admin);

            var response = await _client.PutAsJsonAsync($"{BranchesUrl}/999", ABranch());

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await TitleOf(response)).Should().Be("Branch.NotFound");
        }

        // ── Deactivating ────────────────────────────────────────────────────

        [Fact]
        public async Task Deactivate_TheOnlyActiveBranch_Returns409LastActive()
        {
            AuthenticateAs(Roles.Admin);

            var response = await _client.PostAsync($"{BranchesUrl}/{Branch.MainId}/deactivate", null);

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await TitleOf(response)).Should().Be("Branch.LastActive");
        }

        [Fact]
        public async Task Deactivate_SecondBranch_IsLeftOutOfThePublicListUntilActivated()
        {
            var id = await CreateBranchAsync();

            (await _client.PostAsync($"{BranchesUrl}/{id}/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.OK);

            _client.DefaultRequestHeaders.Clear();
            var publicCodes = (await _client.GetFromJsonAsync<JsonArray>(PublicBranchesUrl))!.Select(b => b!["code"]!.GetValue<string>());
            publicCodes.Should().BeEquivalentTo([Branch.MainCode]);

            AuthenticateAs(Roles.Admin);
            (await _client.PostAsync($"{BranchesUrl}/{id}/activate", null)).StatusCode.Should().Be(HttpStatusCode.OK);
            _client.DefaultRequestHeaders.Clear();
            (await _client.GetFromJsonAsync<JsonArray>(PublicBranchesUrl))!.Should().HaveCount(2);
        }

        [Fact]
        public async Task Book_SlotAtADeactivatedBranch_Returns409SlotNotAvailable()
        {
            // The seeded slot is at MAIN; a second branch lets MAIN be deactivated
            await CreateBranchAsync();
            (await _client.PostAsync($"{BranchesUrl}/{Branch.MainId}/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.OK);

            AuthenticateAs(Roles.Patient, _patientUserId);
            var response = await _client.PostAsJsonAsync("/api/appointments", new BookAppointmentRequest(_slotId, "First visit"));

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        }

        // ── Rooms ───────────────────────────────────────────────────────────

        [Fact]
        public async Task AddRoom_Manager_Returns201AndTheRoomIsListed()
        {
            AuthenticateAs(Roles.Manager);

            var response = await _client.PostAsJsonAsync($"{BranchesUrl}/{Branch.MainId}/rooms",
                new { nameEn = "Room 1", nameAr = "غرفة ١", kind = "Consultation" });

            response.StatusCode.Should().Be(HttpStatusCode.Created);
            var room = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
            room["branchId"]!.GetValue<string>().Should().Be(Branch.MainId.ToString());
            room["kind"]!.GetValue<string>().Should().Be("Consultation");

            AuthenticateAs(Roles.Doctor);
            (await _client.GetFromJsonAsync<JsonArray>($"{BranchesUrl}/{Branch.MainId}/rooms"))!.Should().ContainSingle();
        }

        [Fact]
        public async Task AddRoom_NameTakenAtTheBranch_Returns409RoomNameTaken()
        {
            AuthenticateAs(Roles.Manager);
            var room = new { nameEn = "Room 1", nameAr = "غرفة ١", kind = "Consultation" };
            (await _client.PostAsJsonAsync($"{BranchesUrl}/{Branch.MainId}/rooms", room)).StatusCode.Should().Be(HttpStatusCode.Created);

            var response = await _client.PostAsJsonAsync($"{BranchesUrl}/{Branch.MainId}/rooms", room);

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await TitleOf(response)).Should().Be("Room.NameTaken");
        }

        [Fact]
        public async Task AddRoom_AdminWithoutRoomsManage_Returns403()
        {
            // 02 §6: rooms.manage is the Manager's, at branch scope
            AuthenticateAs(Roles.Admin);

            var response = await _client.PostAsJsonAsync($"{BranchesUrl}/{Branch.MainId}/rooms",
                new { nameEn = "Room 1", nameAr = "غرفة ١", kind = "Consultation" });

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        [Fact]
        public async Task UpdateRoom_Deactivate_Returns200WithIsActiveFalse()
        {
            AuthenticateAs(Roles.Manager);
            var created = await _client.PostAsJsonAsync($"{BranchesUrl}/{Branch.MainId}/rooms",
                new { nameEn = "Room 1", nameAr = "غرفة ١", kind = "Consultation" });
            var roomId = (await created.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<string>();

            var response = await _client.PutAsJsonAsync($"/api/v2/rooms/{roomId}",
                new { nameEn = "Triage", nameAr = "الفرز", kind = "Triage", isActive = false });

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var room = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
            room["isActive"]!.GetValue<bool>().Should().BeFalse();
            room["kind"]!.GetValue<string>().Should().Be("Triage");
        }

        [Fact]
        public async Task ListRooms_UnknownBranch_Returns404()
        {
            AuthenticateAs(Roles.Doctor);

            var response = await _client.GetAsync($"{BranchesUrl}/999/rooms");

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await TitleOf(response)).Should().Be("Branch.NotFound");
        }
    }
}
