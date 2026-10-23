using Microsoft.AspNetCore.Authorization;

namespace Presentation.Authorization
{
    // Requires the permission in any scope (ADR 0002): [HasPermission(Permissions.Appointments.CheckIn)]. Whether
    // the scope covers the record is the service's question, through IAccessGuard. Inherited V1 endpoints keep
    // their role attributes until they are replaced (ADR 0005)
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public sealed class HasPermissionAttribute(string permission) : AuthorizeAttribute(PermissionPolicy.NameFor(permission))
    {
        public string Permission { get; } = permission;
    }
}
