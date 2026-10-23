using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
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
            var user = new ClaimsPrincipal(new ClaimsIdentity(roles.Select(r => new Claim(ClaimTypes.Role, r)), "Test"));
            var requirement = new PermissionRequirement(permission);
            var context = new AuthorizationHandlerContext([requirement], user, resource: null);

            await new PermissionAuthorizationHandler().HandleAsync(context);

            Assert.Equal(expected, context.HasSucceeded);
        }
    }
}
