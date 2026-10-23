using Shared.Authorization;

namespace Services.Implementations
{
    // Scopes from the default matrix (DefaultPermissionMatrix) until P1 stores it per organization
    public class AccessGuard(ICurrentUser currentUser) : IAccessGuard
    {
        public PermissionScope? ScopeOf(string permission)
            => currentUser.IsAuthenticated ? DefaultPermissionMatrix.ScopeFor(currentUser.Roles, permission) : null;

        public Task<bool> CanAccessAsync(string permission, AccessTarget target, CancellationToken ct)
        {
            var allowed = ScopeOf(permission) switch
            {
                PermissionScope.Organization => true,
                PermissionScope.Branch => true, // one branch and no staff assignments until P1 (see IAccessGuard)
                PermissionScope.Own => target.OwnerUserId is { } owner && owner == currentUser.UserId,
                _ => false,
            };
            return Task.FromResult(allowed);
        }
    }
}
