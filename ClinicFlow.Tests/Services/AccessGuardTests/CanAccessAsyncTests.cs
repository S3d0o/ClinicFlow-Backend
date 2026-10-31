using NSubstitute;
using Services.Abstraction.Contracts;
using Shared.Authorization;

namespace ClinicFlow.Tests.Services.AccessGuardTests
{
    public class CanAccessAsyncTests : AccessGuardTestsBase
    {
        private readonly CancellationToken _ct = CancellationToken.None;

        [Fact]
        public async Task CanAccessAsync_OwnScope_OnlyTheUsersOwnRecords()
        {
            SignInAs(Roles.Patient);

            Assert.True(await _sut.CanAccessAsync(Permissions.Appointments.View, new AccessTarget(OwnerUserId: UserId), _ct));
            Assert.False(await _sut.CanAccessAsync(Permissions.Appointments.View, new AccessTarget(OwnerUserId: Guid.NewGuid()), _ct));
            Assert.False(await _sut.CanAccessAsync(Permissions.Appointments.View, new AccessTarget(BranchId: 1), _ct)); // no owner
        }

        [Fact]
        public async Task CanAccessAsync_OrganizationScope_AnyRecord()
        {
            SignInAs(Roles.Admin);

            Assert.True(await _sut.CanAccessAsync(Permissions.Staff.View, new AccessTarget(OwnerUserId: Guid.NewGuid(), BranchId: 7), _ct));
            Assert.True(await _sut.CanAccessAsync(Permissions.Staff.View, new AccessTarget(), _ct));
        }

        [Fact]
        public async Task CanAccessAsync_BranchScopeWithoutAnAssignment_ReachesAnyBranch()
        {
            // A token without role_scope claims keeps P0's reach (EffectiveAccess)
            SignInAs(Roles.Receptionist);

            Assert.True(await _sut.CanAccessAsync(Permissions.Appointments.CheckIn, new AccessTarget(OwnerUserId: Guid.NewGuid(), BranchId: 1), _ct));
        }


        [Theory]
        [InlineData(2, true)]
        [InlineData(3, true)]
        [InlineData(4, false)]
        public async Task CanAccessAsync_BranchScope_OnlyTheAssignedBranches(int branchId, bool expected)
        {
            SignInAsStaff(assignments: At(Roles.Receptionist, 2, 3));

            Assert.Equal(expected, await _sut.CanAccessAsync(Permissions.Appointments.CheckIn, new AccessTarget(BranchId: branchId), _ct));
        }

        [Fact]
        public async Task CanAccessAsync_BranchScope_ARecordOfNoBranch_IsInReach()
        {
            SignInAsStaff(assignments: At(Roles.Manager, 2));

            Assert.True(await _sut.CanAccessAsync(Permissions.Organization.BranchesManage, new AccessTarget(), _ct));
        }

        [Fact]
        public async Task CanAccessAsync_OrganizationWideManager_ReachesEveryBranch()
        {
            SignInAsStaff(assignments: Everywhere(Roles.Manager));

            Assert.True(await _sut.CanAccessAsync(Permissions.Organization.BranchesManage, new AccessTarget(BranchId: 9), _ct));
        }
        [Fact]
        public async Task CanAccessAsync_WithoutThePermission_IsFalse()
        {
            SignInAs(Roles.Patient);

            Assert.False(await _sut.CanAccessAsync(Permissions.Appointments.CheckIn, new AccessTarget(OwnerUserId: UserId), _ct));
        }

        [Fact]
        public async Task CanAccessAsync_Anonymous_IsFalse()
        {
            _currentUser.IsAuthenticated.Returns(false);

            Assert.False(await _sut.CanAccessAsync(Permissions.Appointments.View, new AccessTarget(OwnerUserId: Guid.Empty), _ct));
        }
    }
}
