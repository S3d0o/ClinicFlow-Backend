using Presentation.Controllers.V2;
using System.Security.Claims;

namespace Presentation.Extensions
{
    public static class HttpContextExtensions
    {
        // X-Forwarded-For is applied to RemoteIpAddress by UseForwardedHeaders, and only for
        // trusted proxies. Reading the header directly here would let any client spoof its IP.
        public static string GetClientIpAddress(this HttpContext context)
            => context.Connection.RemoteIpAddress?.ToString() ?? "0.0.0.0";

        // True when the request was routed to an /api/v2 action (a V2ApiController), whose conventions differ (contract §13.1)
        public static bool IsApiV2(this HttpContext context)
            => context.GetEndpoint()?.Metadata.GetMetadata<ApiV2Attribute>() is not null;
    }

    public static class ClaimsPrincipalExtensions
    {
        // Only call from actions that require authentication; the claim is always present there.
        public static Guid GetUserId(this ClaimsPrincipal user)
        {
            var value = user.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("Authenticated user has no NameIdentifier claim.");
            return Guid.Parse(value);
        }

        // For [AllowAnonymous] actions that behave differently for a logged-in caller; null when anonymous
        public static Guid? FindUserId(this ClaimsPrincipal user)
            => Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

        public static string GetRole(this ClaimsPrincipal user)
            => user.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

        // For the inherited routes, which act as Admin, Doctor or Patient: the first of those the user holds, in that
        // order, so a staff member with several roles gets the same answer whatever order the token lists them in
        // (REVIEW.md NL10). Any other role is returned as is, and refused by the route
        public static string GetInheritedRole(this ClaimsPrincipal user)
            => new[] { Roles.Admin, Roles.Doctor, Roles.Patient }.FirstOrDefault(user.IsInRole) ?? user.GetRole();
    }
}
