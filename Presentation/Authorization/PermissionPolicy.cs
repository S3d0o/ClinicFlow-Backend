using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Services.Abstraction.Contracts;
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

    // Why a permission was refused although a role grants it: its module is off for the organization. The result
    // handler turns it into a 403 with the code Subscription.ModuleNotEnabled
    public sealed class ModuleNotEnabledReason(IAuthorizationHandler handler, string module)
        : AuthorizationFailureReason(handler, $"The {module} module is not enabled for this organization")
    {
        public string Module { get; } = module;
    }

    // Permissions come from the roles in the token through the default matrix (ADR 0002 decision 5: tokens carry
    // roles, not permissions), and count only while the permission's module is enabled for the organization
    // (ADR 0008 decision 8). Scoped, because the entitlements are the request's organization's
    public sealed class PermissionAuthorizationHandler(IEntitlements entitlements) : AuthorizationHandler<PermissionRequirement>
    {
        protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
        {
            var roles = context.User.FindAll(ClaimTypes.Role).Select(c => c.Value);
            if (DefaultPermissionMatrix.ScopeFor(roles, requirement.Permission) is null)
                return;

            var module = Modules.Of(requirement.Permission);
            var ct = (context.Resource as HttpContext)?.RequestAborted ?? CancellationToken.None;
            if (await entitlements.IsEnabledAsync(module, ct))
                context.Succeed(requirement);
            else
                context.Fail(new ModuleNotEnabledReason(this, module));
        }
    }
}
