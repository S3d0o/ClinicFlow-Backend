using Domain.Entities.Tenancy;
using Domain.Interfaces;
using Domain.ResultPattern;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Services.Abstraction.Contracts;
using Services.Implementations;
using Shared.Authorization;
using Shared.DTOs.Organization;

namespace ClinicFlow.Tests.Services.BranchCalendarServiceTests
{
    public abstract class BranchCalendarServiceTestsBase
    {
        protected const int BranchId = 7;
        protected const int OtherBranchId = 8;
        protected const int HolidayId = 41;
        protected const string CairoZone = "Africa/Cairo";
        protected const string DubaiZone = "Asia/Dubai";

        // Branch 7's date; branch 8 (another zone) is already a day ahead
        protected static readonly DateOnly Today = new(2026, 10, 18);

        protected readonly IUnitOfWork _uow;
        protected readonly IAccessGuard _accessGuard;
        protected readonly IClock _clock;
        protected readonly ILogger<BranchCalendarService> _logger;
        protected readonly ITransaction _transaction;

        protected readonly BranchCalendarService _sut;

        protected BranchCalendarServiceTestsBase()
        {
            _uow = Substitute.For<IUnitOfWork>();
            _accessGuard = Substitute.For<IAccessGuard>();
            _clock = Substitute.For<IClock>();
            _logger = Substitute.For<ILogger<BranchCalendarService>>();
            _transaction = Substitute.For<ITransaction>();

            _uow.RunsTransactionsWith<Result<IReadOnlyList<DayHoursResponse>>>(_transaction);
            _uow.RunsTransactionsWith<Result<HolidayAddedResponse>>(_transaction);

            _uow.Branches.GetByIdAsync(BranchId, Arg.Any<CancellationToken>()).Returns(new Branch { Id = BranchId, TimeZoneId = CairoZone });
            _uow.Branches.GetByIdAsync(OtherBranchId, Arg.Any<CancellationToken>()).Returns(new Branch { Id = OtherBranchId, TimeZoneId = DubaiZone });
            _uow.Branches.GetTimeZoneIdsAsync(Arg.Any<CancellationToken>())
                .Returns(new Dictionary<int, string> { [BranchId] = CairoZone, [OtherBranchId] = DubaiZone });
            _clock.Today(CairoZone).Returns(Today);
            _clock.Today(DubaiZone).Returns(Today.AddDays(1));

            _uow.WorkingHours.GetByBranchAsync(BranchId, Arg.Any<CancellationToken>()).Returns(new List<BranchWorkingHours>());
            _uow.Appointments.GetUpcomingVisitDaysAsync(Arg.Any<int?>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
                .Returns(new List<(int, DateOnly)>());

            // An organization-wide admin unless a test narrows it
            _accessGuard.ScopeOf(Arg.Any<string>()).Returns(PermissionScope.Organization);
            _accessGuard.CanAccessAsync(Arg.Any<string>(), Arg.Any<AccessTarget>(), Arg.Any<CancellationToken>()).Returns(true);

            _sut = new BranchCalendarService(_uow, _accessGuard, _clock, _logger);
        }

        protected static DayHoursRequest Day(DayOfWeek day, params string[] periods)
            => new((int)day, periods.Select(p => new TimeRangeRequest(TimeOnly.Parse(p[..5]), TimeOnly.Parse(p[6..]))).ToList());

        protected static BranchWorkingHours Stored(int id, DayOfWeek day, string from, string to)
            => new() { Id = id, BranchId = BranchId, Weekday = day, From = TimeOnly.Parse(from), To = TimeOnly.Parse(to) };

        protected static HolidayRequest AHoliday(int? branchId = BranchId, string from = "2026-10-20", string to = "2026-10-21", bool yearly = false)
            => new(branchId, "Maintenance", "صيانة", DateOnly.Parse(from), DateOnly.Parse(to), yearly);
    }
}
