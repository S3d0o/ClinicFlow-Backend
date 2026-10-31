namespace Shared.Authorization
{
    // What a user may do with a permission (docs/v2/02 §4, §6; ADR 0002): the union of their roles, the widest scope
    // winning. A role's Branch grant reaches the branches it is assigned to, or the whole organization when it is
    // assigned there (an organization-wide Manager). Two cells depend on the person rather than the role: some
    // permissions are granted to a Manager only when assigned organization-wide, others only to a medical director.
    //
    // A role with no assignment (a token without role_scope claims: patients, accounts with no staff record yet, and
    // integration tests that send roles only) keeps P0's behaviour: its Branch grant covers every branch
    public static class EffectiveAccess
    {
        // Manager cells "O (org managers only)" of 02 §6
        public static readonly IReadOnlySet<string> OrganizationManagersOnly = new HashSet<string>
        {
            Permissions.Organization.SettingsManage,
            Permissions.Platform.AuditView,
        };

        // Manager cells "O (medical director)" of 02 §6: an organization-wide Manager with IsMedicalDirector
        public static readonly IReadOnlySet<string> MedicalDirectorOnly = new HashSet<string>
        {
            Permissions.Clinical.EncountersView,
            Permissions.Clinical.RecordsBreakGlass,
            Permissions.Reporting.ClinicalReports,
        };

        // Scope: the widest the user holds the permission in, or null. Branches: for a Branch scope, the branches it
        // reaches, null meaning every branch
        public static (PermissionScope? Scope, IReadOnlySet<int>? Branches) Of(
            IReadOnlyCollection<string> roles, IReadOnlyCollection<StaffAssignment> assignments, bool isMedicalDirector, string permission)
        {
            PermissionScope? widest = null;
            HashSet<int>? branches = new();
            var everyBranch = false;

            foreach (var role in roles.Distinct())
            {
                var assigned = assignments.Where(a => a.Role == role).ToList();
                var organizationWide = assigned.Any(a => a.IsOrganizationWide);

                var scope = DefaultPermissionMatrix.ScopeFor([role], permission);
                if (scope is null && role == Roles.Manager && organizationWide
                    && (OrganizationManagersOnly.Contains(permission) || (isMedicalDirector && MedicalDirectorOnly.Contains(permission))))
                    scope = PermissionScope.Organization;

                if (scope == PermissionScope.Branch)
                {
                    if (organizationWide)
                        scope = PermissionScope.Organization;
                    else if (assigned.Count == 0)
                        everyBranch = true;
                    else
                        branches.UnionWith(assigned.SelectMany(a => a.BranchIds!));
                }

                if (scope is { } granted && (widest is null || granted > widest))
                    widest = granted;
            }

            return widest == PermissionScope.Branch ? (widest, everyBranch ? null : branches) : (widest, null);
        }
    }
}
