using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using NSubstitute;
using Services.Abstraction.Contracts;
using Presentation.Authorization;
using Shared.Authorization;
using System.Security.Claims;

namespace ClinicFlow.Tests.Authorization
{
    // [HasPermission] → a "permission:<name>" policy → PermissionAuthorizationHandler, checked against the matrix
    public class PermissionPolicyTests
    {
        private static readonly AuthorizationPolicy Fallback = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();

        private readonly PermissionPolicyProvider _provider = new(Options.Create(new AuthorizationOptions { FallbackPolicy = Fallback }));

        [Fact]
        public void HasPermission_UsesThePermissionPolicyName()
            => Assert.Equal("permission:appointments.check_in", new HasPermissionAttribute(Permissions.Appointments.CheckIn).Policy);

        [Fact]
        public async Task GetPolicyAsync_ForAPermission_RequiresASignedInUserAndThePermission()
        {
            var policy = await _provider.GetPolicyAsync(PermissionPolicy.NameFor(Permissions.Staff.View));

            Assert.NotNull(policy);
            Assert.Contains(policy.Requirements, r => r is PermissionRequirement { Permission: Permissions.Staff.View });
            Assert.Contains(policy.Requirements, r => r is Microsoft.AspNetCore.Authorization.Infrastructure.DenyAnonymousAuthorizationRequirement);
        }

        [Fact]
        public async Task GetPolicyAsync_ForAnUnknownPermission_Throws()
        {
            // A typo in [HasPermission] fails loudly instead of locking or opening the endpoint
            await Assert.ThrowsAsync<InvalidOperationException>(() => _provider.GetPolicyAsync("permission:appointments.chek_in"));
        }

        [Fact]
        public async Task GetFallbackPolicyAsync_IsStillTheConfiguredOne()
        {
            // The provider replaces the default one, so the "secure by default" fallback (REVIEW.md NM5) must survive
            Assert.Same(Fallback, await _provider.GetFallbackPolicyAsync());
            Assert.Null(await _provider.GetPolicyAsync("SomeOtherPolicy"));
        }

        [Theory]
        [InlineData(Permissions.Staff.View, true, Roles.Admin)]
        [InlineData(Permissions.Appointments.View, true, Roles.Patient)]
        [InlineData(Permissions.Appointments.View, false, Roles.Admin)]
        [InlineData(Permissions.Appointments.CheckIn, true, Roles.Patient, Roles.Receptionist)]
        [InlineData(Permissions.Appointments.CheckIn, false)]
        public async Task Handler_SucceedsOnlyWhenARoleGrantsThePermission(string permission, bool expected, params string[] roles)
        {
            var context = ContextFor(permission, roles);

            await new PermissionAuthorizationHandler(AllModulesEnabled()).HandleAsync(context);

            Assert.Equal(expected, context.HasSucceeded);
        }

        [Fact]
        public async Task Handler_ModuleOff_FailsWithTheModuleAsReason()
        {
            // ADR 0008 decision 8: a role's grant counts only while the permission's module is on
            var entitlements = Substitute.For<IEntitlements>();
            entitlements.IsEnabledAsync(Modules.FrontDesk, Arg.Any<CancellationToken>()).Returns(false);
            var context = ContextFor(Permissions.Appointments.QueueView, Roles.Receptionist);

            await new PermissionAuthorizationHandler(entitlements).HandleAsync(context);

            Assert.False(context.HasSucceeded);
            Assert.True(context.HasFailed);
            var reason = Assert.IsType<ModuleNotEnabledReason>(Assert.Single(context.FailureReasons));
            Assert.Equal(Modules.FrontDesk, reason.Module);
        }

        [Fact]
        public async Task Handler_ModuleOn_Succeeds()
        {
            var entitlements = Substitute.For<IEntitlements>();
            entitlements.IsEnabledAsync(Modules.FrontDesk, Arg.Any<CancellationToken>()).Returns(true);
            var context = ContextFor(Permissions.Appointments.QueueView, Roles.Receptionist);

            await new PermissionAuthorizationHandler(entitlements).HandleAsync(context);

            Assert.True(context.HasSucceeded);
        }

        [Fact]
        public async Task Handler_NoRoleGrantsThePermission_NeverAsksTheEntitlements()
        {
            // An ordinary 403, not "not in your plan": the module is irrelevant when no role grants the permission
            var entitlements = Substitute.For<IEntitlements>();
            var context = ContextFor(Permissions.Billing.InvoicesView, Roles.Admin, Roles.Nurse);

            await new PermissionAuthorizationHandler(entitlements).HandleAsync(context);

            Assert.False(context.HasSucceeded);
            Assert.False(context.HasFailed);
            await entitlements.DidNotReceiveWithAnyArgs().IsEnabledAsync(default!, default);
        }

        [Fact]
        public async Task Handler_AsksAboutThePermissionsModule()
        {
            var entitlements = AllModulesEnabled();
            var context = ContextFor(Permissions.Billing.InvoicesView, Roles.Accountant);

            await new PermissionAuthorizationHandler(entitlements).HandleAsync(context);

            await entitlements.Received(1).IsEnabledAsync(Modules.Billing, Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData("Manager=*", true)]
        [InlineData("Manager=2", false)]
        public async Task Handler_OrgManagersOnlyCell_FollowsTheRoleScopeClaim(string roleScope, bool expected)
        {
            // 02 §6: org.settings.manage is a Manager's only when assigned organization-wide (P1 PR 4)
            var user = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Role, Roles.Manager), new Claim(ClinicClaims.RoleScope, roleScope)], "Test"));
            var context = new AuthorizationHandlerContext([new PermissionRequirement(Permissions.Organization.SettingsManage)], user, resource: null);

            await new PermissionAuthorizationHandler(AllModulesEnabled()).HandleAsync(context);

            Assert.Equal(expected, context.HasSucceeded);
        }

        private static AuthorizationHandlerContext ContextFor(string permission, params string[] roles)
        {
            var user = new ClaimsPrincipal(new ClaimsIdentity(roles.Select(r => new Claim(ClaimTypes.Role, r)), "Test"));
            return new AuthorizationHandlerContext([new PermissionRequirement(permission)], user, resource: null);
        }

        private static IEntitlements AllModulesEnabled()
        {
            var entitlements = Substitute.For<IEntitlements>();
            entitlements.IsEnabledAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
            return entitlements;
        }
    }
}
