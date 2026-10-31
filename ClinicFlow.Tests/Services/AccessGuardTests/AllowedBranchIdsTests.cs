using Shared.Authorization;

namespace ClinicFlow.Tests.Services.AccessGuardTests
{
    // The branches a list is filtered to (P1 PR 4; staff directory, PR 7)
    public class AllowedBranchIdsTests : AccessGuardTestsBase
    {
        [Fact]
        public void AllowedBranchIds_OrganizationScope_IsEveryBranch()
        {
            SignInAs(Roles.Admin);

            Assert.Null(_sut.AllowedBranchIds(Permissions.Staff.View));
        }

        [Fact]
        public void AllowedBranchIds_BranchScope_IsTheUnionOfTheRolesBranches()
        {
            SignInAsStaff(assignments: [At(Roles.Doctor, 1), At(Roles.Nurse, 3)]);

            Assert.Equal([1, 3], _sut.AllowedBranchIds(Permissions.Staff.View)!.Order());
        }

        [Fact]
        public void AllowedBranchIds_BranchScopeWithoutAnAssignment_IsEveryBranch()
        {
            SignInAs(Roles.Receptionist);

            Assert.Null(_sut.AllowedBranchIds(Permissions.Staff.View));
        }

        [Theory]
        [InlineData(Roles.Admin)]   // not held
        [InlineData(Roles.Patient)] // own records only
        public void AllowedBranchIds_NotHeldOrOwnOnly_IsNone(string role)
        {
            SignInAs(role);

            Assert.Empty(_sut.AllowedBranchIds(Permissions.Patients.View)!);
        }
    }
}
