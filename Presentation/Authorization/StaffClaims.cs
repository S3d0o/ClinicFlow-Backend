using System.Globalization;
using System.Security.Claims;

namespace Presentation.Authorization
{
    // Reads the roles and the V2 staff claims (Shared.Authorization.ClinicClaims) of a principal, for HttpCurrentUser
    // and the permission handler, which judges the principal it is given rather than the current request's
    public static class StaffClaims
    {
        public static IReadOnlyList<string> Roles(ClaimsPrincipal principal)
            => principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

        // A malformed role_scope is ignored, leaving that role without an assignment
        public static IReadOnlyList<StaffAssignment> Assignments(ClaimsPrincipal principal)
            => principal.FindAll(ClinicClaims.RoleScope).Select(c => ClinicClaims.ParseRoleScope(c.Value)).OfType<StaffAssignment>().ToList();

        public static bool IsMedicalDirector(ClaimsPrincipal principal)
            => principal.FindFirstValue(ClinicClaims.MedicalDirector) == "true";

        public static int? StaffMemberId(ClaimsPrincipal principal)
            => int.TryParse(principal.FindFirstValue(ClinicClaims.StaffMember), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;

        public static PermissionScope? ScopeOf(ClaimsPrincipal principal, string permission)
            => EffectiveAccess.Of(Roles(principal), Assignments(principal), IsMedicalDirector(principal), permission).Scope;
    }
}
