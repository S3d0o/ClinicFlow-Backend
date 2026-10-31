using Shared.Authorization;

namespace Services.Abstraction.Contracts
{
    // The resource half of permission checks (ADR 0002 decision 3): [HasPermission] lets a user reach an endpoint
    // when they hold the permission in any scope, and the service then asks whether that scope covers the record
    public interface IAccessGuard
    {
        // The widest scope the current user holds the permission in (the union of their roles), or null
        PermissionScope? ScopeOf(string permission);

        // The branches the permission reaches, for filtering lists: null for every branch (Organization scope, or a role
        // without assignments), empty when the user doesn't hold it or only holds it for their own records
        IReadOnlySet<int>? AllowedBranchIds(string permission);
        // Whether the current user may use the permission on the record. Organization covers every record (the
        // tenant filter already keeps them to the user's organization). Own covers records whose owner is the
        // user. Branch covers the records of the branches the user's roles are assigned to (P1 PR 4), and records
        // of no branch
        Task<bool> CanAccessAsync(string permission, AccessTarget target, CancellationToken ct);
    }

    // What a record says about who may reach it: its owner's user id (a patient's or doctor's own records) and
    // its branch, either of which may be unknown
    public record AccessTarget(Guid? OwnerUserId = null, int? BranchId = null);
}
