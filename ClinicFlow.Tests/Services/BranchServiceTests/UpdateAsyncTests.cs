using NSubstitute;
using Services.Abstraction.Contracts;
using Shared.Authorization;
using Shared.Errors;

namespace ClinicFlow.Tests.Services.BranchServiceTests
{
    public class UpdateAsyncTests : BranchServiceTestsBase
    {
        [Fact]
        public async Task UpdateAsync_ValidRequest_ReplacesTheBranchAndSaves()
        {
            var branch = ABranch();
            _uow.Branches.GetByIdAsync(BranchId, Arg.Any<CancellationToken>()).Returns(branch);

            var result = await _sut.UpdateAsync(BranchId, ARequest(), CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal("NSR", branch.Code);
            Assert.Equal("مدينة نصر", branch.NameAr);
            Assert.Equal("١٢ مكرم عبيد", branch.AddressAr);
            Assert.Equal("01001234567", branch.Phone);
            Assert.Equal(BranchId, result.Value.Id);
            await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task UpdateAsync_KeepsItsOwnCode_ChecksUniquenessAgainstOtherBranchesOnly()
        {
            _uow.Branches.GetByIdAsync(BranchId, Arg.Any<CancellationToken>()).Returns(ABranch());

            await _sut.UpdateAsync(BranchId, ARequest("HLP"), CancellationToken.None);

            await _uow.Branches.Received(1).CodeExistsAsync("HLP", BranchId, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task UpdateAsync_UnknownBranch_ReturnsNotFound()
        {
            var result = await _sut.UpdateAsync(BranchId, ARequest(), CancellationToken.None);

            Assert.Equal("Branch.NotFound", Assert.Single(result.Errors).Code);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task UpdateAsync_CodeTakenByAnotherBranch_ReturnsCodeTaken()
        {
            _uow.Branches.GetByIdAsync(BranchId, Arg.Any<CancellationToken>()).Returns(ABranch());
            _uow.Branches.CodeExistsAsync("NSR", BranchId, Arg.Any<CancellationToken>()).Returns(true);

            var result = await _sut.UpdateAsync(BranchId, ARequest(), CancellationToken.None);

            Assert.Equal(BranchErrors.CodeTaken, Assert.Single(result.Errors));
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task UpdateAsync_BranchOutsideTheUsersScope_ReturnsOutOfScope()
        {
            _uow.Branches.GetByIdAsync(BranchId, Arg.Any<CancellationToken>()).Returns(ABranch());
            _accessGuard.CanAccessAsync(Permissions.Organization.BranchesManage,
                Arg.Is<AccessTarget>(t => t!.BranchId == BranchId), Arg.Any<CancellationToken>()).Returns(false);

            var result = await _sut.UpdateAsync(BranchId, ARequest(), CancellationToken.None);

            Assert.Equal(AccessErrors.OutOfScope, Assert.Single(result.Errors));
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }
}
