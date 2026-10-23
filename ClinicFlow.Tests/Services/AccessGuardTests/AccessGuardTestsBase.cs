using NSubstitute;
using Services.Abstraction.Contracts;
using Services.Implementations;

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
        }
    }
}
