using Domain.Entities.IdentityModule;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Services.Abstraction.Contracts;
using Services.Implementations;
using Shared.Authorization;
using Shared.Errors;

namespace ClinicFlow.Tests.Services.MeServiceTests
{
    // GET /api/v2/me (P1 PR 4): the token's roles and the permissions they give, for the frontend's menus
    public class GetAsyncTests
    {
        private readonly UserManager<ApplicationUser> _userManager = Substitute.For<UserManager<ApplicationUser>>(
            Substitute.For<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
        private readonly IEntitlements _entitlements = Substitute.For<IEntitlements>();
        private readonly ApplicationUser _user = new()
        {
            Id = Guid.NewGuid(), FirstName = "Hala", LastName = "Adel", Email = "hala@test.com", PhoneNumber = "01001234567",
            EmailConfirmed = true,
        };
        private readonly MeService _sut;

        public GetAsyncTests()
        {
            _userManager.FindByIdAsync(_user.Id.ToString()).Returns(_user);
            _currentUser.IsAuthenticated.Returns(true);
            _currentUser.UserId.Returns(_user.Id);
            _entitlements.IsEnabledAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
            _sut = new MeService(_userManager, _currentUser, new AccessGuard(_currentUser), _entitlements, Substitute.For<ILogger<MeService>>());
        }

        private void SignedInAs(bool medicalDirector, params StaffAssignment[] assignments)
        {
            _currentUser.Roles.Returns(assignments.Select(a => a.Role).ToList());
            _currentUser.Assignments.Returns(assignments);
            _currentUser.IsMedicalDirector.Returns(medicalDirector);
        }

        [Fact]
        public async Task GetAsync_AStaffMember_ReturnsTheProfileRolesAndPermissions()
        {
            SignedInAs(false, new StaffAssignment(Roles.Manager, new HashSet<int> { 3, 2 }));

            var me = (await _sut.GetAsync(CancellationToken.None)).Value;

            Assert.Equal(("Hala Adel", "01001234567", "hala@test.com", true), (me.FullName, me.Phone, me.Email, me.EmailConfirmed));
            var role = Assert.Single(me.Roles);
            Assert.Equal((Roles.Manager, PermissionScope.Branch), (role.Role, role.Scope));
            Assert.Equal([2, 3], role.BranchIds);
            var branches = me.Permissions.Single(p => p.Permission == Permissions.Organization.BranchesManage);
            Assert.Equal(PermissionScope.Branch, branches.Scope);
            Assert.Equal([2, 3], branches.BranchIds);
            Assert.DoesNotContain(me.Permissions, p => p.Permission == Permissions.Organization.SettingsManage); // org managers only
        }

        [Fact]
        public async Task GetAsync_AnOrganizationWideManager_HasOrganizationScopeAndTheOrgManagerCells()
        {
            SignedInAs(true, new StaffAssignment(Roles.Manager, null));

            var me = (await _sut.GetAsync(CancellationToken.None)).Value;

            Assert.Equal(PermissionScope.Organization, Assert.Single(me.Roles).Scope);
            Assert.Null(me.Roles[0].BranchIds);
            Assert.Contains(me.Permissions, p => p.Permission == Permissions.Organization.SettingsManage && p.Scope == PermissionScope.Organization);
            Assert.True(me.IsMedicalDirector);
        }

        [Fact]
        public async Task GetAsync_APermissionWhoseModuleIsOff_IsLeftOut()
        {
            SignedInAs(false, new StaffAssignment(Roles.Receptionist, new HashSet<int> { 1 }));
            _entitlements.IsEnabledAsync(Modules.FrontDesk, Arg.Any<CancellationToken>()).Returns(false);

            var me = (await _sut.GetAsync(CancellationToken.None)).Value;

            Assert.DoesNotContain(me.Permissions, p => p.Permission == Permissions.Appointments.QueueView);
            Assert.Contains(me.Permissions, p => p.Permission == Permissions.Appointments.CheckIn);
        }

        [Fact]
        public async Task GetAsync_APatient_HasOwnScopes()
        {
            _currentUser.Roles.Returns([Roles.Patient]);
            _currentUser.Assignments.Returns(new List<StaffAssignment>());

            var me = (await _sut.GetAsync(CancellationToken.None)).Value;

            Assert.Equal((Roles.Patient, PermissionScope.Organization), (me.Roles[0].Role, me.Roles[0].Scope));
            Assert.All(me.Permissions, p => Assert.Equal(PermissionScope.Own, p.Scope));
        }

        [Fact]
        public async Task GetAsync_AnAccountThatNoLongerExists_ReturnsProfileNotFound()
        {
            _userManager.FindByIdAsync(_user.Id.ToString()).Returns((ApplicationUser?)null);

            Assert.Equal(ProfileErrors.NotFound, Assert.Single((await _sut.GetAsync(CancellationToken.None)).Errors));
        }
    }
}
