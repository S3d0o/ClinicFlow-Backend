using Domain.Entities.Tenancy;
using NSubstitute;
using Services.Abstraction.Contracts;
using Shared.Authorization;
using Shared.Errors;

namespace ClinicFlow.Tests.Services.BranchCalendarServiceTests
{
    public class RemoveHolidayAsyncTests : BranchCalendarServiceTestsBase
    {
        private static BranchHoliday Holiday(int? branchId = BranchId)
            => new() { Id = HolidayId, BranchId = branchId, From = Today, To = Today };

        [Fact]
        public async Task RemoveHolidayAsync_KnownHoliday_RemovesIt()
        {
            var holiday = Holiday();
            _uow.Holidays.GetByIdAsync(HolidayId, Arg.Any<CancellationToken>()).Returns(holiday);

            var result = await _sut.RemoveHolidayAsync(HolidayId, CancellationToken.None);

            Assert.True(result.IsSuccess);
            _uow.Holidays.Received(1).Remove(holiday);
            await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task RemoveHolidayAsync_UnknownHoliday_ReturnsNotFound()
        {
            var result = await _sut.RemoveHolidayAsync(HolidayId, CancellationToken.None);

            Assert.Equal(BranchErrors.HolidayNotFound(HolidayId), Assert.Single(result.Errors));
        }

        [Fact]
        public async Task RemoveHolidayAsync_BranchOutsideTheCallersScope_ReturnsOutOfScope()
        {
            _uow.Holidays.GetByIdAsync(HolidayId, Arg.Any<CancellationToken>()).Returns(Holiday());
            _accessGuard.CanAccessAsync(Permissions.Organization.BranchesManage, new AccessTarget(BranchId: BranchId), Arg.Any<CancellationToken>())
                .Returns(false);

            var result = await _sut.RemoveHolidayAsync(HolidayId, CancellationToken.None);

            Assert.Equal(AccessErrors.OutOfScope, Assert.Single(result.Errors));
            _uow.Holidays.DidNotReceive().Remove(Arg.Any<BranchHoliday>());
        }

        [Fact]
        public async Task RemoveHolidayAsync_ForEveryBranchWithBranchScopeOnly_ReturnsOutOfScope()
        {
            _uow.Holidays.GetByIdAsync(HolidayId, Arg.Any<CancellationToken>()).Returns(Holiday(branchId: null));
            _accessGuard.ScopeOf(Permissions.Organization.BranchesManage).Returns(PermissionScope.Branch);

            var result = await _sut.RemoveHolidayAsync(HolidayId, CancellationToken.None);

            Assert.Equal(AccessErrors.OutOfScope, Assert.Single(result.Errors));
            _uow.Holidays.DidNotReceive().Remove(Arg.Any<BranchHoliday>());
        }
    }
}
