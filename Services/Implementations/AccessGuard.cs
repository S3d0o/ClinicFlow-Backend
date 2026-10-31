namespace Services.Implementations
{
    // Scopes from the default matrix (DefaultPermissionMatrix) and the user's staff assignments in the token
    // (EffectiveAccess): a Branch grant reaches the branches its role is assigned to, or the organization for an
    // organization-wide assignment. Roles without assignments keep P0's every-branch reach
    public class AccessGuard(ICurrentUser currentUser) : IAccessGuard
    {
        public PermissionScope? ScopeOf(string permission) => Access(permission).Scope;

        public IReadOnlySet<int>? AllowedBranchIds(string permission)
            => Access(permission) switch
            {
                (PermissionScope.Organization, _) => null,
                (PermissionScope.Branch, var branches) => branches,
                _ => new HashSet<int>(),
            };

        public Task<bool> CanAccessAsync(string permission, AccessTarget target, CancellationToken ct)
        {
            var (scope, branches) = Access(permission);
            var allowed = scope switch
            {
                PermissionScope.Organization => true,
                // A record of no particular branch is in reach of every branch scope, as in P0
                PermissionScope.Branch => target.BranchId is not { } branchId || branches is null || branches.Contains(branchId),
                PermissionScope.Own => target.OwnerUserId is { } owner && owner == currentUser.UserId,
                _ => false,
            };
            return Task.FromResult(allowed);
        }

        private (PermissionScope? Scope, IReadOnlySet<int>? Branches) Access(string permission)
            => currentUser.IsAuthenticated
                ? EffectiveAccess.Of(currentUser.Roles, currentUser.Assignments, currentUser.IsMedicalDirector, permission)
                : (null, null);
    }
}
