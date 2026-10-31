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
            await _uow.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>()); // the branch, then its hours (they need its id)
            await _transaction.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task CreateAsync_NoBranchHasHours_OpensAroundTheClockEveryDay()
        {
            List<BranchWorkingHours>? added = null;
            _uow.WorkingHours.AddRange(Arg.Do<IEnumerable<BranchWorkingHours>>(h => added = h.ToList()));

            await _sut.CreateAsync(ARequest(), CancellationToken.None);

            Assert.NotNull(added);
            Assert.Equal(7, added.Count);
            Assert.Equal(Enum.GetValues<DayOfWeek>(), added.Select(h => h.Weekday).Order());
            Assert.All(added, h => Assert.Equal((TimeOnly.MinValue, new TimeOnly(23, 59)), (h.From, h.To)));
        }

        [Fact]
        public async Task CreateAsync_FirstBranchHasHours_CopiesItsWeek()
        {
            _uow.WorkingHours.GetUsualWeekAsync(Arg.Any<CancellationToken>()).Returns(
            [
                new BranchWorkingHours { Id = 1, BranchId = 1, Weekday = DayOfWeek.Saturday, From = new(9, 0), To = new(15, 0) },
                new BranchWorkingHours { Id = 2, BranchId = 1, Weekday = DayOfWeek.Saturday, From = new(16, 0), To = new(22, 0) },
            ]);
            List<BranchWorkingHours>? added = null;
            _uow.WorkingHours.AddRange(Arg.Do<IEnumerable<BranchWorkingHours>>(h => added = h.ToList()));

            await _sut.CreateAsync(ARequest(), CancellationToken.None);

            Assert.NotNull(added);
            Assert.Equal([(new TimeOnly(9, 0), new TimeOnly(15, 0)), (new TimeOnly(16, 0), new TimeOnly(22, 0))],
                added.Select(h => (h.From, h.To)));
            Assert.All(added, h => Assert.Equal((0, DayOfWeek.Saturday), (h.Id, h.Weekday))); // new rows, not the first branch's
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
