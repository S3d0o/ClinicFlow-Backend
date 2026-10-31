using Domain.Entities.IdentityModule;
using Domain.Entities.Staff;
using Domain.Enums;
using Domain.Interfaces;
using Microsoft.AspNetCore.Identity;
using NSubstitute;
using Services.Abstraction.Contracts;
using Services.Implementations;
using Shared.Authorization;

namespace ClinicFlow.Tests.Services.UserAccessServiceTests
{
    // Where a token's roles come from (P1 PR 4): staff assignments for staff, Identity roles for everyone else
    public class GetAsyncTests
    {
        private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
        private readonly UserManager<ApplicationUser> _userManager = Substitute.For<UserManager<ApplicationUser>>(
            Substitute.For<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
        private readonly ApplicationUser _user = new() { Id = Guid.NewGuid() };
        private readonly UserAccessService _sut;

        public GetAsyncTests()
        {
            _tenant.OrganizationId.Returns(1);
            _sut = new UserAccessService(_uow, _userManager, _tenant);
        }

        private void IdentityRoles(params string[] roles) => _userManager.GetRolesAsync(_user).Returns(roles.ToList());

        private void Staff(StaffStatus status, bool medicalDirector, params (string Role, int? BranchId)[] rows)
            => _uow.Staff.GetByUserIdAsync(_user.Id, Arg.Any<CancellationToken>()).Returns(new StaffMember
            {
                Id = 12,
                UserId = _user.Id,
                Status = status,
                IsMedicalDirector = medicalDirector,
                Assignments = rows.Select(r => new StaffRoleAssignment { Role = r.Role, BranchId = r.BranchId }).ToList(),
            });

        [Fact]
        public async Task GetAsync_NotStaff_KeepsTheIdentityRoles()
        {
            IdentityRoles(Roles.Patient);

            var access = await _sut.GetAsync(_user, CancellationToken.None);

            Assert.Equal([Roles.Patient], access.Roles);
            Assert.Equal((1, (int?)null, false), (access.OrganizationId, access.StaffMemberId, access.IsMedicalDirector));
            Assert.Empty(access.Assignments);
        }

        [Fact]
        public async Task GetAsync_ActiveStaff_TakesTheRolesFromTheAssignments()
        {
            // The Identity Admin row is no longer the source; a branch role at two branches is one assignment
            IdentityRoles(Roles.Admin);
            Staff(StaffStatus.Active, medicalDirector: true, (Roles.Manager, null), (Roles.Doctor, 1), (Roles.Doctor, 3));

            var access = await _sut.GetAsync(_user, CancellationToken.None);

            Assert.Equal([Roles.Manager, Roles.Doctor], access.Roles);
            Assert.Equal(12, access.StaffMemberId);
            Assert.True(access.IsMedicalDirector);
            Assert.True(access.Assignments.Single(a => a.Role == Roles.Manager).IsOrganizationWide);
            Assert.Equal([1, 3], access.Assignments.Single(a => a.Role == Roles.Doctor).BranchIds!.Order());
        }

        [Fact]
        public async Task GetAsync_StaffWhoIsAlsoAPatient_KeepsThePatientRole()
        {
            IdentityRoles(Roles.Patient, Roles.Doctor);
            Staff(StaffStatus.Active, medicalDirector: false, (Roles.Doctor, 1));

            Assert.Equal([Roles.Doctor, Roles.Patient], (await _sut.GetAsync(_user, CancellationToken.None)).Roles);
        }

        [Theory]
        [InlineData(StaffStatus.Deactivated)]
        [InlineData(StaffStatus.Invited)]
        public async Task GetAsync_StaffNotActive_HasNoStaffRoles(StaffStatus status)
        {
            IdentityRoles(Roles.Doctor);
            Staff(status, medicalDirector: true, (Roles.Doctor, 1));

            var access = await _sut.GetAsync(_user, CancellationToken.None);

            Assert.Empty(access.Roles);
            Assert.Empty(access.Assignments);
            Assert.False(access.IsMedicalDirector);
        }

        [Fact]
        public void PrimaryRole_IsTheFirstInRoleOrder()
            => Assert.Equal(Roles.Admin, new UserAccess([Roles.Doctor, Roles.Admin], 1, null, false, []).PrimaryRole);
    }
}
