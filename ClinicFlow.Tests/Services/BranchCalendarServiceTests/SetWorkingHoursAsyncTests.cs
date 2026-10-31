using Domain.Entities.Tenancy;
using NSubstitute;
using Services.Abstraction.Contracts;
using Shared.Errors;
using Shared.DTOs.Organization;

namespace ClinicFlow.Tests.Services.BranchCalendarServiceTests
{
    public class SetWorkingHoursAsyncTests : BranchCalendarServiceTestsBase
    {
        private List<BranchWorkingHours>? _added;
        private List<BranchWorkingHours>? _removed;

        public SetWorkingHoursAsyncTests()
        {
            _uow.WorkingHours.AddRange(Arg.Do<IEnumerable<BranchWorkingHours>>(h => _added = h.ToList()));
            _uow.WorkingHours.RemoveRange(Arg.Do<IEnumerable<BranchWorkingHours>>(h => _removed = h.ToList()));
        }

        [Fact]
        public async Task SetWorkingHoursAsync_AValidWeek_SavesItAndReturnsSevenDaysSundayFirst()
        {
            var result = await _sut.SetWorkingHoursAsync(BranchId,
                [Day(DayOfWeek.Saturday, "16:00-22:00", "09:00-15:00"), Day(DayOfWeek.Friday, "13:00-21:00")], CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal([0, 1, 2, 3, 4, 5, 6], result.Value.Select(d => d.Weekday));
            Assert.Empty(result.Value[0].Ranges); // not sent: closed
            Assert.Equal([new TimeRangeResponse(new(9, 0), new(15, 0)), new TimeRangeResponse(new(16, 0), new(22, 0))], result.Value[6].Ranges);
            Assert.Equal(3, _added!.Count);
            Assert.All(_added, h => Assert.Equal(BranchId, h.BranchId));
            await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
            await _transaction.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task SetWorkingHoursAsync_SomePeriodsUnchanged_OnlyReplacesTheChangedOnes()
        {
            // The audit log should show the real edit: Friday's evening moved, Saturday kept, Sunday closed
            var keptSaturday = Stored(1, DayOfWeek.Saturday, "09:00", "15:00");
            var oldFriday = Stored(2, DayOfWeek.Friday, "13:00", "21:00");
            var oldSunday = Stored(3, DayOfWeek.Sunday, "09:00", "15:00");
            _uow.WorkingHours.GetByBranchAsync(BranchId, Arg.Any<CancellationToken>()).Returns([keptSaturday, oldFriday, oldSunday]);

            await _sut.SetWorkingHoursAsync(BranchId,
                [Day(DayOfWeek.Saturday, "09:00-15:00"), Day(DayOfWeek.Friday, "14:00-21:00")], CancellationToken.None);

            Assert.Equal([oldFriday, oldSunday], _removed!);
            var added = Assert.Single(_added!);
            Assert.Equal((DayOfWeek.Friday, new TimeOnly(14, 0), new TimeOnly(21, 0)), (added.Weekday, added.From, added.To));
        }

        [Theory]
        [InlineData("15:00-09:00")]
        [InlineData("09:00-09:00")]
        [InlineData("09:00-15:00", "14:00-22:00")]
        public async Task SetWorkingHoursAsync_PeriodsOutOfOrderOrOverlapping_ReturnsInvalidHours(params string[] periods)
        {
            var result = await _sut.SetWorkingHoursAsync(BranchId, [Day(DayOfWeek.Monday, periods)], CancellationToken.None);

            Assert.Equal(BranchErrors.InvalidHours, Assert.Single(result.Errors));
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task SetWorkingHoursAsync_AWeekdayTwice_ReturnsInvalidHours()
        {
            var result = await _sut.SetWorkingHoursAsync(BranchId,
                [Day(DayOfWeek.Monday, "09:00-12:00"), Day(DayOfWeek.Monday, "16:00-20:00")], CancellationToken.None);

            Assert.Equal(BranchErrors.InvalidHours, Assert.Single(result.Errors));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(7)]
        public async Task SetWorkingHoursAsync_AnUnknownWeekday_ReturnsInvalidHours(int weekday)
        {
            var result = await _sut.SetWorkingHoursAsync(BranchId, [new DayHoursRequest(weekday, [])], CancellationToken.None);

            Assert.Equal(BranchErrors.InvalidHours, Assert.Single(result.Errors));
        }

        [Fact]
        public async Task SetWorkingHoursAsync_UnknownBranch_ReturnsNotFound()
        {
            var result = await _sut.SetWorkingHoursAsync(99, [], CancellationToken.None);

            Assert.Equal("Branch.NotFound", Assert.Single(result.Errors).Code);
        }

        [Fact]
        public async Task SetWorkingHoursAsync_BranchOutsideTheCallersScope_ReturnsOutOfScope()
        {
            _accessGuard.CanAccessAsync(Arg.Any<string>(), Arg.Any<AccessTarget>(), Arg.Any<CancellationToken>()).Returns(false);

            var result = await _sut.SetWorkingHoursAsync(BranchId, [], CancellationToken.None);

            Assert.Equal(AccessErrors.OutOfScope, Assert.Single(result.Errors));
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }
}
