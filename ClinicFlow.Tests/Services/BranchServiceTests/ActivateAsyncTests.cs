using NSubstitute;
using Shared.Authorization;
using Shared.Errors;

namespace ClinicFlow.Tests.Services.BranchServiceTests
{
    public class ActivateAsyncTests : BranchServiceTestsBase
    {
        [Fact]
        public async Task ActivateAsync_DeactivatedBranch_ActivatesIt()
        {
            var branch = ABranch(isActive: false);
            _uow.Branches.GetByIdAsync(BranchId, Arg.Any<CancellationToken>()).Returns(branch);

            var result = await _sut.ActivateAsync(BranchId, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.True(branch.IsActive);
            await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ActivateAsync_AlreadyActive_ReturnsItUnchanged()
        {
            _uow.Branches.GetByIdAsync(BranchId, Arg.Any<CancellationToken>()).Returns(ABranch());

            var result = await _sut.ActivateAsync(BranchId, CancellationToken.None);

            Assert.True(result.Value.IsActive);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ActivateAsync_UnknownBranch_ReturnsNotFound()
        {
            var result = await _sut.ActivateAsync(BranchId, CancellationToken.None);

            Assert.Equal("Branch.NotFound", Assert.Single(result.Errors).Code);
        }

        [Fact]
        public async Task ActivateAsync_PermissionHeldForBranchesOnly_ReturnsOutOfScope()
        {
            _uow.Branches.GetByIdAsync(BranchId, Arg.Any<CancellationToken>()).Returns(ABranch(isActive: false));
            _accessGuard.ScopeOf(Permissions.Organization.BranchesManage).Returns(PermissionScope.Branch);

            var result = await _sut.ActivateAsync(BranchId, CancellationToken.None);

            Assert.Equal(AccessErrors.OutOfScope, Assert.Single(result.Errors));
        }
    }
}
