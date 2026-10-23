using NSubstitute;
using Shared.Authorization;

namespace ClinicFlow.Tests.Services.AccessGuardTests
{
    public class ScopeOfTests : AccessGuardTestsBase
    {
        [Theory]
        [InlineData(Roles.Patient, Permissions.Appointments.View, PermissionScope.Own)]
        [InlineData(Roles.Receptionist, Permissions.Appointments.CheckIn, PermissionScope.Branch)]
        [InlineData(Roles.Admin, Permissions.Staff.Invite, PermissionScope.Organization)]
        public void ScopeOf_AGrantedPermission_IsTheMatrixScope(string role, string permission, PermissionScope expected)
        {
            SignInAs(role);

            Assert.Equal(expected, _sut.ScopeOf(permission));
        }

        [Fact]
        public void ScopeOf_SeveralRoles_IsTheWidest()
        {
            SignInAs(Roles.Doctor, Roles.Manager);

            Assert.Equal(PermissionScope.Branch, _sut.ScopeOf(Permissions.Appointments.Cancel)); // Doctor: Own, Manager: Branch
        }

        [Fact]
        public void ScopeOf_APermissionNoRoleGrants_IsNull()
        {
            SignInAs(Roles.Admin);

            Assert.Null(_sut.ScopeOf(Permissions.Clinical.EncountersView)); // ADR 0002: no clinical content for Admin
        }

        [Fact]
        public void ScopeOf_Anonymous_IsNull()
        {
            _currentUser.IsAuthenticated.Returns(false);
            _currentUser.Roles.Returns([Roles.Admin]); // roles alone don't count without a signed-in user

            Assert.Null(_sut.ScopeOf(Permissions.Staff.View));
        }
    }
}
