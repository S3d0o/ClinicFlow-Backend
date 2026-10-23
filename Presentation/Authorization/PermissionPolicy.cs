using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Shared.Authorization;
using System.Security.Claims;

namespace Presentation.Authorization
{
    public static class PermissionPolicy
    {
        public const string Prefix = "permission:";

        public static string NameFor(string permission) => Prefix + permission;
    }

    public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

    // Builds a policy for any "permission:<name>" on demand, so no policy is registered per permission. Every other
    // name, the fallback policy included, comes from the configured options as before
    public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
    {
        public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
        {
            if (!policyName.StartsWith(PermissionPolicy.Prefix, StringComparison.Ordinal))
                return await base.GetPolicyAsync(policyName);

            var permission = policyName[PermissionPolicy.Prefix.Length..];
            // An unknown name is a typo, and a typo must not quietly lock an endpoint (or worse, open one)
            if (!Permissions.All.Contains(permission))
                throw new InvalidOperationException($"Unknown permission '{permission}' in [HasPermission]");

            return new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(permission))
                .Build();
        }
    }

    // Permissions come from the roles in the token through the default matrix (ADR 0002 decision 5: tokens carry
    // roles, not permissions)
    public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
        {
            var roles = context.User.FindAll(ClaimTypes.Role).Select(c => c.Value);
            if (DefaultPermissionMatrix.ScopeFor(roles, requirement.Permission) is not null)
                context.Succeed(requirement);
            return Task.CompletedTask;
        }
    }
}
