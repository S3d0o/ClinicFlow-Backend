using NSubstitute;
using Services.Abstraction.Contracts;
using Services.Implementations;
using Shared.Authorization;

namespace ClinicFlow.Tests.Services.AccessGuardTests
{
    public abstract class AccessGuardTestsBase
    {
        // Dependencies
        protected readonly ICurrentUser _currentUser;
        protected static readonly Guid UserId = Guid.NewGuid();

        // System Under Test
        protected readonly AccessGuard _sut;

        protected AccessGuardTestsBase()
        {
            _currentUser = Substitute.For<ICurrentUser>();
            _sut = new AccessGuard(_currentUser);
        }

        protected void SignInAs(params string[] roles)
        {
            _currentUser.IsAuthenticated.Returns(true);
            _currentUser.UserId.Returns(UserId);
            _currentUser.Roles.Returns(roles);
            _currentUser.Assignments.Returns(new List<StaffAssignment>());
        }

        // A staff member with these assignments (and their roles), as the token's role_scope claims give them
        protected void SignInAsStaff(bool isMedicalDirector = false, params StaffAssignment[] assignments)
        {
            SignInAs(assignments.Select(a => a.Role).Distinct().ToArray());
            _currentUser.Assignments.Returns(assignments);
            _currentUser.IsMedicalDirector.Returns(isMedicalDirector);
        }

        protected static StaffAssignment At(string role, params int[] branchIds) => new(role, branchIds.ToHashSet());
        protected static StaffAssignment Everywhere(string role) => new(role, null);
    }
}
