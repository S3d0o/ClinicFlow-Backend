using Presentation.Extensions;
using Services.Abstraction.Contracts;
using System.Security.Claims;

namespace Presentation.Authorization
{
    public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
    {
        private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

        public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

        public Guid UserId => IsAuthenticated
            ? Principal!.GetUserId()
            : throw new InvalidOperationException("The current request is anonymous.");

        public IReadOnlyList<string> Roles => IsAuthenticated
            ? Principal!.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList()
            : [];

        public string? IpAddress => accessor.HttpContext?.GetClientIpAddress();

        public bool IsInRole(string role) => IsAuthenticated && Principal!.IsInRole(role);
    }
}
