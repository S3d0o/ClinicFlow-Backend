using Domain.Entities.Tenancy;
using NSubstitute;
using Shared.Authorization;
using Shared.Errors;

namespace ClinicFlow.Tests.Services.BranchServiceTests
{
    public class CreateAsyncTests : BranchServiceTestsBase
    {
        [Fact]
        public async Task CreateAsync_NewCode_AddsAnActiveBranchInATransaction()
        {
            _uow.Branches.CodeExistsAsync("NSR", null, Arg.Any<CancellationToken>()).Returns(false);

            var result = await _sut.CreateAsync(ARequest(), CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal("NSR", result.Value.Code);
            Assert.Equal("12 Makram Ebeid", result.Value.AddressEn);
            Assert.Equal("Africa/Cairo", result.Value.TimeZone);
            Assert.True(result.Value.IsActive);
            _uow.Branches.Received(1).Add(Arg.Is<Branch>(b => b!.Code == "NSR" && b.IsActive && b.TimeZoneId == "Africa/Cairo"));
            await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
            await _transaction.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task CreateAsync_CodeTaken_ReturnsCodeTaken()
        {
            _uow.Branches.CodeExistsAsync("NSR", null, Arg.Any<CancellationToken>()).Returns(true);

            var result = await _sut.CreateAsync(ARequest(), CancellationToken.None);

            Assert.Equal(BranchErrors.CodeTaken, Assert.Single(result.Errors));
            _uow.Branches.DidNotReceive().Add(Arg.Any<Branch>());
        }

        [Theory]
        [InlineData(PermissionScope.Branch)]
        [InlineData(null)]
        public async Task CreateAsync_PermissionNotHeldOrganizationWide_ReturnsOutOfScope(PermissionScope? scope)
        {
            _accessGuard.ScopeOf(Permissions.Organization.BranchesManage).Returns(scope);

            var result = await _sut.CreateAsync(ARequest(), CancellationToken.None);

            Assert.Equal(AccessErrors.OutOfScope, Assert.Single(result.Errors));
            _uow.Branches.DidNotReceive().Add(Arg.Any<Branch>());
        }
    }
}
