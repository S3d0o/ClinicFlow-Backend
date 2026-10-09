using System.Security.Claims;

namespace Presentation.Extensions
{
    public static class HttpContextExtensions
    {
        // X-Forwarded-For is applied to RemoteIpAddress by UseForwardedHeaders, and only for
        // trusted proxies. Reading the header directly here would let any client spoof its IP.
        public static string GetClientIpAddress(this HttpContext context)
            => context.Connection.RemoteIpAddress?.ToString() ?? "0.0.0.0";
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

        public static string GetRole(this ClaimsPrincipal user)
            => user.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
    }
}
