using NSubstitute;
using Shared.Authorization;
using Shared.Errors;

namespace ClinicFlow.Tests.Services.BranchServiceTests
{
    public class DeactivateAsyncTests : BranchServiceTestsBase
    {
        [Fact]
        public async Task DeactivateAsync_OneOfSeveralActiveBranches_DeactivatesIt()
        {
            var branch = ABranch();
            _uow.Branches.GetByIdAsync(BranchId, Arg.Any<CancellationToken>()).Returns(branch);
            _uow.Branches.CountActiveAsync(Arg.Any<CancellationToken>()).Returns(2);

            var result = await _sut.DeactivateAsync(BranchId, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.False(result.Value.IsActive);
            Assert.False(branch.IsActive);
            await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task DeactivateAsync_TheOnlyActiveBranch_ReturnsLastActive()
        {
            var branch = ABranch();
            _uow.Branches.GetByIdAsync(BranchId, Arg.Any<CancellationToken>()).Returns(branch);
            _uow.Branches.CountActiveAsync(Arg.Any<CancellationToken>()).Returns(1);

            var result = await _sut.DeactivateAsync(BranchId, CancellationToken.None);

            Assert.Equal(BranchErrors.LastActive, Assert.Single(result.Errors));
            Assert.True(branch.IsActive);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task DeactivateAsync_AlreadyDeactivated_ReturnsItUnchanged()
        {
            _uow.Branches.GetByIdAsync(BranchId, Arg.Any<CancellationToken>()).Returns(ABranch(isActive: false));

            var result = await _sut.DeactivateAsync(BranchId, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.False(result.Value.IsActive);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task DeactivateAsync_UnknownBranch_ReturnsNotFound()
        {
            var result = await _sut.DeactivateAsync(BranchId, CancellationToken.None);

            Assert.Equal("Branch.NotFound", Assert.Single(result.Errors).Code);
        }

        [Fact]
        public async Task DeactivateAsync_PermissionHeldForBranchesOnly_ReturnsOutOfScope()
        {
            _uow.Branches.GetByIdAsync(BranchId, Arg.Any<CancellationToken>()).Returns(ABranch());
            _accessGuard.ScopeOf(Permissions.Organization.BranchesManage).Returns(PermissionScope.Branch);

            var result = await _sut.DeactivateAsync(BranchId, CancellationToken.None);

            Assert.Equal(AccessErrors.OutOfScope, Assert.Single(result.Errors));
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }
}
