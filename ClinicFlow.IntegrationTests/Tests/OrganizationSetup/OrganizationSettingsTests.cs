using ClinicFlow.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Shared.Authorization;
using Shared.DTOs.Appointment;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace ClinicFlow.IntegrationTests.Tests.OrganizationSetup
{
    // The organization's settings over HTTP (P1 PR 3; API_CONTRACT.md §13.24): defaults, partial changes with their
    // audit entries, bounds and unknown keys, and an appointment rule that reads a changed window at once
    [Collection(nameof(IntegrationTestCollection))]
    public class OrganizationSettingsTests(ClinicFlowApiFactory factory) : IAsyncLifetime
    {
        private const string SettingsUrl = "/api/v2/organization/settings";

        private readonly HttpClient _client = factory.CreateClient();
        private Guid _patientUserId;
        private int _appointmentId;

        public async Task InitializeAsync()
        {
            await factory.ResetDataBaseAsync();
            _client.DefaultRequestHeaders.Clear();

            await factory.SeedAsync(async db =>
            {
                (_patientUserId, _, var slotId, _appointmentId) = await SeedDataHelper.SeedBaseAppointmentAsync(db);

                // Two days out at 10:00: always inside a 72-hour window, never inside the default 2 hours
                var slot = await db.AppointmentSlots.SingleAsync(s => s.Id == slotId);
                slot.Date = factory.ClinicToday.AddDays(2);
                slot.StartsAtUtc = SeedDataHelper.SlotUtc(slot.Date, slot.StartTime);
                slot.EndsAtUtc = SeedDataHelper.SlotUtc(slot.Date, slot.EndTime);
                await db.SaveChangesAsync();
            });
        }

        public Task DisposeAsync() => Task.CompletedTask;

        private void AuthenticateAs(string role, Guid? userId = null)
        {
            _client.DefaultRequestHeaders.Clear();
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, (userId ?? Guid.NewGuid()).ToString());
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserRoleHeader, role);
        }

        private Task<HttpResponseMessage> PatchAsync(object body) => _client.PatchAsJsonAsync(SettingsUrl, body);

        [Fact]
        public async Task Get_AnyStaff_ReturnsEveryKeyWithItsDefault()
        {
            AuthenticateAs(Roles.Receptionist);

            var response = await _client.GetAsync(SettingsUrl);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var settings = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
            settings.Count.Should().Be(14);
            settings["cancellationWindowHours"]!.GetValue<int>().Should().Be(2);
            settings["noShowGraceMinutes"]!.GetValue<int>().Should().Be(15);
            settings["depositAmount"]!.GetValue<decimal>().Should().Be(100);
            settings["shareRecordsWithinBranch"]!.GetValue<bool>().Should().BeTrue();
        }

        [Fact]
        public async Task Get_Patient_Returns403()
        {
            AuthenticateAs(Roles.Patient);

            (await _client.GetAsync(SettingsUrl)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        [Fact]
        public async Task Patch_Admin_ChangesOnlyTheKeysSentAndAuditsEach()
        {
            AuthenticateAs(Roles.Admin);

            var response = await PatchAsync(new { walkInCap = 8, depositEnabled = true });

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var settings = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
            settings["walkInCap"]!.GetValue<int>().Should().Be(8);
            settings["depositEnabled"]!.GetValue<bool>().Should().BeTrue();
            settings["cancellationWindowHours"]!.GetValue<int>().Should().Be(2);

            var again = (await (await _client.GetAsync(SettingsUrl)).Content.ReadFromJsonAsync<JsonObject>())!;
            again["walkInCap"]!.GetValue<int>().Should().Be(8);

            await factory.SeedAsync(async db =>
            {
                var entries = await db.AuditEntries.AsNoTracking().Where(e => e.EntityType == "OrganizationSetting").ToListAsync();
                entries.Should().HaveCount(2);
                entries.Should().Contain(e => e.Changes.Contains("walkInCap") && e.Changes.Contains("8"));
            });
        }

        [Fact]
        public async Task Patch_AValueOutOfBounds_Returns400KeyedByTheSetting()
        {
            AuthenticateAs(Roles.Admin);

            var response = await PatchAsync(new { noShowGraceMinutes = 2 });

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await response.Content.ReadFromJsonAsync<JsonObject>())!["errors"]!.AsObject().ContainsKey("noShowGraceMinutes").Should().BeTrue();
        }

        [Fact]
        public async Task Patch_AnUnknownKey_Returns400AndChangesNothing()
        {
            AuthenticateAs(Roles.Admin);

            var response = await PatchAsync(new { walkInCap = 8, freeParking = true });

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var walkInCap = (await (await _client.GetAsync(SettingsUrl)).Content.ReadFromJsonAsync<JsonObject>())!["walkInCap"]!.GetValue<int>();
            walkInCap.Should().Be(5);
        }

        [Fact]
        public async Task Patch_Doctor_Returns403()
        {
            AuthenticateAs(Roles.Doctor);

            (await PatchAsync(new { walkInCap = 8 })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        [Fact]
        public async Task CancelByPatient_AfterTheWindowGrows_ReturnsCancellationWindowExpired()
        {
            // The default 2 hours would allow it; the clinic now wants 72
            AuthenticateAs(Roles.Admin);
            (await PatchAsync(new { cancellationWindowHours = 72 })).StatusCode.Should().Be(HttpStatusCode.OK);
            AuthenticateAs(Roles.Patient, _patientUserId);

            var response = await _client.PostAsJsonAsync($"/api/appointments/{_appointmentId}/cancel", new CancelAppointmentRequest(null));

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await response.Content.ReadAsStringAsync()).Should().Contain("Appointment.CancellationWindowExpired");
        }
    }
}
