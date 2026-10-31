using Domain.Entities.Tenancy;
using NSubstitute;
using Services.Abstraction.Contracts;
using Shared.Authorization;
using Shared.Errors;

namespace ClinicFlow.Tests.Services.BranchCalendarServiceTests
{
    public class AddHolidayAsyncTests : BranchCalendarServiceTestsBase
    {
        private BranchHoliday? _added;
        private List<DateOnly>? _withdrawnDays;

        public AddHolidayAsyncTests()
        {
            _uow.Holidays.Add(Arg.Do<BranchHoliday>(h => { h.Id = HolidayId; _added = h; }));
            _uow.Slots.WithdrawFreeSlotsAsync(Arg.Any<int?>(), Arg.Do<IReadOnlyCollection<DateOnly>>(d => _withdrawnDays = d.ToList()), Arg.Any<CancellationToken>())
                .Returns(5);
        }

        [Fact]
        public async Task AddHolidayAsync_ForABranch_SavesItWithdrawsItsFreeSlotsAndCountsItsVisits()
        {
            _uow.Appointments.GetUpcomingVisitDaysAsync(BranchId, Today, Arg.Any<CancellationToken>()).Returns(
            [
                (BranchId, new DateOnly(2026, 10, 20)),
                (BranchId, new DateOnly(2026, 10, 21)),
                (BranchId, new DateOnly(2026, 10, 22)), // the day after
            ]);

            var result = await _sut.AddHolidayAsync(AHoliday(), CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(2, result.Value.AffectedAppointments);
            Assert.Equal(HolidayId, result.Value.Holiday.Id);
            Assert.Equal(BranchId, result.Value.Holiday.BranchId);
            Assert.Equal(("Maintenance", false), (_added!.NameEn, _added.RecursYearly));
            Assert.Equal([new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 21)], _withdrawnDays);
            await _uow.Slots.Received(1).WithdrawFreeSlotsAsync(BranchId, Arg.Any<IReadOnlyCollection<DateOnly>>(), Arg.Any<CancellationToken>());
            await _transaction.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task AddHolidayAsync_ForEveryBranch_CountsEachBranchsVisitsFromItsOwnToday()
        {
            // Branch 8 is a day ahead: its visit on 18 Oct has passed, branch 7's hasn't
            _uow.Appointments.GetUpcomingVisitDaysAsync(null, Today, Arg.Any<CancellationToken>()).Returns(
            [
                (BranchId, Today),
                (OtherBranchId, Today),
                (OtherBranchId, Today.AddDays(1)),
            ]);

            var result = await _sut.AddHolidayAsync(AHoliday(branchId: null, from: "2026-10-18", to: "2026-10-19"), CancellationToken.None);

            Assert.Equal(2, result.Value.AffectedAppointments);
            await _uow.Slots.Received(1).WithdrawFreeSlotsAsync(null, Arg.Any<IReadOnlyCollection<DateOnly>>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task AddHolidayAsync_Yearly_WithdrawsItsDaysWithinTheComingYear()
        {
            var result = await _sut.AddHolidayAsync(AHoliday(from: "2020-10-25", to: "2020-10-25", yearly: true), CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal([new DateOnly(2026, 10, 25)], _withdrawnDays);
        }

        [Fact]
        public async Task AddHolidayAsync_EntirelyInThePast_WithdrawsNothing()
        {
            var result = await _sut.AddHolidayAsync(AHoliday(from: "2026-01-07", to: "2026-01-07"), CancellationToken.None);

            Assert.Equal(0, result.Value.AffectedAppointments);
            await _uow.Slots.DidNotReceive().WithdrawFreeSlotsAsync(Arg.Any<int?>(), Arg.Any<IReadOnlyCollection<DateOnly>>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task AddHolidayAsync_UnknownBranch_ReturnsNotFound()
        {
            var result = await _sut.AddHolidayAsync(AHoliday(branchId: 99), CancellationToken.None);

            Assert.Equal("Branch.NotFound", Assert.Single(result.Errors).Code);
            _uow.Holidays.DidNotReceive().Add(Arg.Any<BranchHoliday>());
        }

        [Fact]
        public async Task AddHolidayAsync_BranchOutsideTheCallersScope_ReturnsOutOfScope()
        {
            _accessGuard.CanAccessAsync(Arg.Any<string>(), Arg.Any<AccessTarget>(), Arg.Any<CancellationToken>()).Returns(false);

            var result = await _sut.AddHolidayAsync(AHoliday(), CancellationToken.None);

            Assert.Equal(AccessErrors.OutOfScope, Assert.Single(result.Errors));
            _uow.Holidays.DidNotReceive().Add(Arg.Any<BranchHoliday>());
        }

        [Theory]
        [InlineData(PermissionScope.Branch)]
        [InlineData(null)]
        public async Task AddHolidayAsync_ForEveryBranchWithoutOrganizationScope_ReturnsOutOfScope(PermissionScope? scope)
        {
            _accessGuard.ScopeOf(Permissions.Organization.BranchesManage).Returns(scope);

            var result = await _sut.AddHolidayAsync(AHoliday(branchId: null), CancellationToken.None);

            Assert.Equal(AccessErrors.OutOfScope, Assert.Single(result.Errors));
            _uow.Holidays.DidNotReceive().Add(Arg.Any<BranchHoliday>());
        }
    }
}
