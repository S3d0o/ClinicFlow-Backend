// Presentation/Filters/HttpContextExtensions.cs
namespace Presentation.Extensions
{
    public static class HttpContextExtensions
    {
        public static string GetClientIpAddress(this HttpContext context)
        {
            // Check reverse proxy header first (nginx, K8s ingress)
            var forwarded = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrEmpty(forwarded))
                return forwarded.Split(',')[0].Trim();

            return context.Connection.RemoteIpAddress?.ToString() ?? "0.0.0.0";
        }
    }

}

