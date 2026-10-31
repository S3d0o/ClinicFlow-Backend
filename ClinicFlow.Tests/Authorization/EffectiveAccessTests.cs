using Shared.Authorization;

namespace ClinicFlow.Tests.Authorization
{
    // docs/v2/02 §4 and §6 with staff assignments (P1 PR 4): the widest grant of the user's roles, Branch grants
    // reaching the assigned branches, and the cells that depend on the person
    public class EffectiveAccessTests
    {
        private static (PermissionScope? Scope, IReadOnlySet<int>? Branches) Of(string permission, bool medicalDirector = false,
            params StaffAssignment[] assignments)
            => EffectiveAccess.Of(assignments.Select(a => a.Role).Distinct().ToList(), assignments, medicalDirector, permission);

        private static StaffAssignment At(string role, params int[] branches) => new(role, branches.ToHashSet());
        private static StaffAssignment Everywhere(string role) => new(role, null);

        [Fact]
        public void Of_ABranchGrantAssignedToBranches_ReachesThem()
        {
            var (scope, branches) = Of(Permissions.Organization.BranchesManage, assignments: At(Roles.Manager, 2, 5));

            Assert.Equal(PermissionScope.Branch, scope);
            Assert.Equal([2, 5], branches!.Order());
        }

        [Fact]
        public void Of_ABranchGrantAssignedOrganizationWide_IsOrganization()
            => Assert.Equal((PermissionScope.Organization, null), Of(Permissions.Organization.BranchesManage, assignments: Everywhere(Roles.Manager)));

        [Fact]
        public void Of_ARoleWithoutAnAssignment_ReachesEveryBranch()
        {
            // A token without role_scope claims: P0's behaviour
            var (scope, branches) = EffectiveAccess.Of([Roles.Manager], [], false, Permissions.Organization.BranchesManage);

            Assert.Equal(PermissionScope.Branch, scope);
            Assert.Null(branches);
        }

        [Fact]
        public void Of_TwoRolesAtDifferentBranches_ReachesBoth()
        {
            var (scope, branches) = Of(Permissions.Staff.View, assignments: [At(Roles.Doctor, 1), At(Roles.Nurse, 3)]);

            Assert.Equal(PermissionScope.Branch, scope);
            Assert.Equal([1, 3], branches!.Order());
        }

        [Fact]
        public void Of_TheWidestRoleWins()
            => Assert.Equal(PermissionScope.Organization, Of(Permissions.Staff.View, assignments: [At(Roles.Doctor, 1), Everywhere(Roles.Admin)]).Scope);

        [Fact]
        public void Of_AnOrganizationGrant_IgnoresTheAssignedBranches()
            => Assert.Equal((PermissionScope.Organization, null), Of(Permissions.Catalog.SpecialtiesManage, assignments: At(Roles.Manager, 2)));

        [Theory]
        [InlineData(true, PermissionScope.Organization)]
        [InlineData(false, null)]
        public void Of_OrganizationManagersOnly_NeedsAnOrganizationWideManager(bool organizationWide, PermissionScope? expected)
        {
            var manager = organizationWide ? Everywhere(Roles.Manager) : At(Roles.Manager, 2);

            Assert.Equal(expected, Of(Permissions.Organization.SettingsManage, assignments: manager).Scope);
            Assert.Equal(expected, Of(Permissions.Platform.AuditView, assignments: manager).Scope);
        }

        [Theory]
        [InlineData(true, true, PermissionScope.Organization)]
        [InlineData(true, false, null)]  // a medical director assigned to one branch only
        [InlineData(false, true, null)]  // an organization-wide manager who isn't the medical director
        public void Of_MedicalDirectorOnly_NeedsAnOrganizationWideMedicalDirector(bool medicalDirector, bool organizationWide, PermissionScope? expected)
        {
            var manager = organizationWide ? Everywhere(Roles.Manager) : At(Roles.Manager, 2);

            Assert.Equal(expected, Of(Permissions.Clinical.EncountersView, medicalDirector, manager).Scope);
        }

        [Fact]
        public void Of_TheMedicalDirectorFlag_DoesNothingForOtherRoles()
            => Assert.Null(Of(Permissions.Clinical.EncountersView, medicalDirector: true, Everywhere(Roles.Admin)).Scope);

        [Fact]
        public void Of_OwnGrant_StaysOwn()
            => Assert.Equal((PermissionScope.Own, null), EffectiveAccess.Of([Roles.Patient], [], false, Permissions.Appointments.View));
    }
}
