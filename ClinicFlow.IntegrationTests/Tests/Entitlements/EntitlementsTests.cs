using ClinicFlow.IntegrationTests.Infrastructure;
using Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Persistence.AppData;
using Presentation.Authorization;
using Shared.Authorization;
using Shared.DTOs.Entitlements;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json.Nodes;

namespace ClinicFlow.IntegrationTests.Tests.Entitlements
{
    // Plans, modules and their enforcement (docs/v2/adr/0008, plans/R1.md M1). The migration puts the organization on
    // the Full plan with recruitment added; the platform switches move it, and the next request sees the change because
    // the save invalidates the cached entitlements
    [Collection(nameof(IntegrationTestCollection))]
    public class EntitlementsTests(ClinicFlowApiFactory factory) : IAsyncLifetime
    {
        private const string EntitlementsUrl = "/api/v2/organization/entitlements";
        private const string ModulesUrl = "/api/v2/organization/modules";
        private const string PlansUrl = "/api/v2/plans";
        private const string PlanUrl = "/api/v2/platform/subscription/plan";
        private const string ModuleUrl = "/api/v2/platform/subscription/modules";

        private static readonly string[] Core = ["ORG", "STF", "ACC", "SVC", "SCH", "APT", "NTF", "AUD"];

        private readonly HttpClient _client = factory.CreateClient();

        public async Task InitializeAsync()
        {
            await factory.ResetDataBaseAsync();
            _client.DefaultRequestHeaders.Clear();

            await factory.SeedAsync(async db =>
            {
                await SeedDataHelper.SeedDoctorAsync(db, "approved");
                await SeedDataHelper.SeedDoctorAsync(db, "pending", approved: false);
                await SeedDataHelper.SeedDoctorAsync(db, "suspended", active: false);
            });
        }

        public Task DisposeAsync() => Task.CompletedTask;

        private void AuthenticateAs(string role)
        {
            _client.DefaultRequestHeaders.Clear();
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, Guid.NewGuid().ToString());
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserRoleHeader, role);
        }

        private async Task<string[]> EnabledModulesAsync()
        {
            var response = await _client.GetAsync(ModulesUrl);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            return (await response.Content.ReadFromJsonAsync<string[]>())!;
        }

        private static async Task<string?> TitleOf(HttpResponseMessage response)
            => (await response.Content.ReadFromJsonAsync<JsonObject>())?["title"]?.GetValue<string>();

        // ── GET /api/v2/organization/entitlements ──────────────────────────

        [Fact]
        public async Task GetEntitlements_AsAdmin_ReturnsTheSeededSubscription()
        {
            AuthenticateAs(Roles.Admin);

            var response = await _client.GetAsync(EntitlementsUrl);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
            body["planCode"]!.GetValue<string>().Should().Be("Full");
            body["status"]!.GetValue<string>().Should().Be("Active");
            body["currentPeriodEnd"].Should().BeNull();
            body["modules"]!.AsArray().Select(m => m!.GetValue<string>()).Should().Equal(
                [.. Core, "FD", "BIL", "EMR", "INS", "RPT", "PAT", "REC"]); // catalog order; BOT stays off
            body["overrides"]!.ToJsonString().Should().Be("""[{"module":"REC","enabled":true}]""");
            body["usage"]!.ToJsonString().Should().Be(
                """[{"key":"branches","included":3,"used":1},{"key":"doctors","included":15,"used":1},{"key":"phoneMessagesPerMonth","included":2500,"used":0}]""");
        }

        [Fact]
        public async Task GetEntitlements_Anonymous_Returns401()
            => (await _client.GetAsync(EntitlementsUrl)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        [Theory]
        [InlineData(Roles.Patient)]
        [InlineData(Roles.Doctor)]
        public async Task GetEntitlements_WithoutOrgSettingsManage_Returns403(string role)
        {
            AuthenticateAs(role);

            var response = await _client.GetAsync(EntitlementsUrl);

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await TitleOf(response)).Should().NotBe("Subscription.ModuleNotEnabled"); // a role's 403, not the plan's
        }

        // ── GET /api/v2/organization/modules and /api/v2/plans ─────────────

        [Fact]
        public async Task GetModules_Anonymous_ReturnsTheEnabledModulesInCatalogOrder()
            => (await EnabledModulesAsync()).Should().Equal([.. Core, "FD", "BIL", "EMR", "INS", "RPT", "PAT", "REC"]);

        [Fact]
        public async Task ListPlans_AsAdmin_ReturnsStarterClinicAndFull()
        {
            AuthenticateAs(Roles.Admin);

            var response = await _client.GetAsync(PlansUrl);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response.Content.ReadAsStringAsync()).Should().Be(
                """[{"code":"Starter","name":"Starter","modules":[],"limits":{"branches":1,"doctors":2,"phoneMessagesPerMonth":300}},""" +
                """{"code":"Clinic","name":"Clinic","modules":["FD","BIL","RPT","PAT"],"limits":{"branches":1,"doctors":5,"phoneMessagesPerMonth":1000}},""" +
                """{"code":"Full","name":"Full","modules":["FD","BIL","EMR","INS","RPT","PAT"],"limits":{"branches":3,"doctors":15,"phoneMessagesPerMonth":2500}}]""");
        }

        [Fact]
        public async Task ListPlans_AsPatient_Returns403()
        {
            AuthenticateAs(Roles.Patient);

            (await _client.GetAsync(PlansUrl)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        // ── PUT /api/v2/platform/subscription/plan ─────────────────────────

        [Fact]
        public async Task ChangePlan_ToStarter_TheNextRequestSeesOnlyTheCoreAndTheExtra()
        {
            await EnabledModulesAsync(); // caches the Full plan's modules
            AuthenticateAs(Roles.Admin);

            var response = await _client.PutAsJsonAsync(PlanUrl, new ChangePlanRequest("Starter"));

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await EnabledModulesAsync()).Should().Equal([.. Core, "REC"]); // recruitment is still an extra
            var body = (await (await _client.GetAsync(EntitlementsUrl)).Content.ReadFromJsonAsync<JsonObject>())!;
            body["planCode"]!.GetValue<string>().Should().Be("Starter");
            body["usage"]!.AsArray()[1]!["included"]!.GetValue<int>().Should().Be(2);
        }

        [Fact]
        public async Task ChangePlan_DropsAnExtraTheNewPlanIncludes_AndIsAudited()
        {
            AuthenticateAs(Roles.Admin);
            (await _client.PutAsJsonAsync(PlanUrl, new ChangePlanRequest("Starter"))).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await _client.PutAsJsonAsync(ModuleUrl, new SetModuleRequest(Modules.Billing, true))).StatusCode.Should().Be(HttpStatusCode.NoContent);

            (await _client.PutAsJsonAsync(PlanUrl, new ChangePlanRequest("Clinic"))).StatusCode.Should().Be(HttpStatusCode.NoContent);

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ClinicDbContext>();
            (await db.OrganizationSubscriptions.SingleAsync()).PlanId.Should().Be(2);
            (await db.OrganizationModuleOverrides.Select(o => o.ModuleCode).ToListAsync())
                .Should().Equal("REC"); // Clinic includes billing, so it is no longer an extra; recruitment still is

            var planChanges = await db.AuditEntries.Where(e => e.EntityType == "OrganizationSubscription").OrderBy(e => e.Id).ToListAsync();
            planChanges.Select(e => JsonNode.Parse(e.Changes)!["PlanId"]!.ToJsonString())
                .Should().Equal("""{"old":3,"new":1}""", """{"old":1,"new":2}""");
            (await db.AuditEntries.Where(e => e.EntityType == "OrganizationModuleOverride" && e.EntityId == "1,BIL")
                    .OrderBy(e => e.Id).Select(e => e.Action).ToListAsync())
                .Should().Equal(AuditAction.Create, AuditAction.Delete);
        }

        [Fact]
        public async Task ChangePlan_UnknownPlan_Returns404()
        {
            AuthenticateAs(Roles.Admin);

            var response = await _client.PutAsJsonAsync(PlanUrl, new ChangePlanRequest("Gold"));

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await TitleOf(response)).Should().Be("Subscription.PlanNotFound");
        }

        [Fact]
        public async Task ChangePlan_EmptyCode_Returns400()
        {
            AuthenticateAs(Roles.Admin);

            (await _client.PutAsJsonAsync(PlanUrl, new ChangePlanRequest(""))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Theory]
        [InlineData(Roles.Patient)]
        [InlineData(Roles.Doctor)]
        public async Task ChangePlan_WithoutOrgSettingsManage_Returns403(string role)
        {
            AuthenticateAs(role);

            (await _client.PutAsJsonAsync(PlanUrl, new ChangePlanRequest("Starter"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        [Fact]
        public async Task ChangePlan_Anonymous_Returns401()
            => (await _client.PutAsJsonAsync(PlanUrl, new ChangePlanRequest("Starter"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // ── PUT /api/v2/platform/subscription/modules ──────────────────────

        [Fact]
        public async Task SetModule_FrontDeskOff_TheQueuePermissionIsRefusedForTheModule()
        {
            AuthenticateAs(Roles.Admin);

            var response = await _client.PutAsJsonAsync(ModuleUrl, new SetModuleRequest(Modules.FrontDesk, false));

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await EnabledModulesAsync()).Should().NotContain("FD");

            // The app's permission handler: the receptionist's role grants queue.view, but its module is off
            var result = await AuthorizeAsync(Roles.Receptionist, Permissions.Appointments.QueueView);
            result.Succeeded.Should().BeFalse();
            result.Failure!.FailureReasons.OfType<ModuleNotEnabledReason>().Single().Module.Should().Be(Modules.FrontDesk);

            (await AuthorizeAsync(Roles.Receptionist, Permissions.Appointments.CheckIn)).Succeeded.Should().BeTrue(); // core APT
        }

        [Fact]
        public async Task SetModule_BillingOffWhileInsuranceIsOn_Returns409()
        {
            AuthenticateAs(Roles.Admin);

            var response = await _client.PutAsJsonAsync(ModuleUrl, new SetModuleRequest(Modules.Billing, false));

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await TitleOf(response)).Should().Be("Subscription.ModuleRequired");
        }

        [Fact]
        public async Task SetModule_ACoreModule_Returns400()
        {
            AuthenticateAs(Roles.Admin);

            var response = await _client.PutAsJsonAsync(ModuleUrl, new SetModuleRequest(Modules.Appointments, false));

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        // ── Wiring ─────────────────────────────────────────────────────────

        [Fact]
        public void TheAuthorizationResultHandler_AnswersModuleRefusalsWithTheSubscriptionCode()
            => factory.Services.GetRequiredService<IAuthorizationMiddlewareResultHandler>()
                .Should().BeOfType<SubscriptionAuthorizationResultHandler>();

        private async Task<AuthorizationResult> AuthorizeAsync(string role, string permission)
        {
            using var scope = factory.Services.CreateScope();
            var policy = await scope.ServiceProvider.GetRequiredService<IAuthorizationPolicyProvider>()
                .GetPolicyAsync(PermissionPolicy.NameFor(permission));
            var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "Test"));
            return await scope.ServiceProvider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, null, policy!);
        }
    }
}
