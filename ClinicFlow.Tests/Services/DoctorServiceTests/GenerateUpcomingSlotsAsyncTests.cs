using Domain.Entities.AppModule;
using Domain.Entities.Tenancy;
using Domain.Time;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace ClinicFlow.Tests.Services.DoctorServiceTests
{
    // REVIEW.md H8: the daily job rolls every active schedule forward to the 14-day window
    public class GenerateUpcomingSlotsAsyncTests : DoctorServiceTestsBase
    {
        private readonly CancellationToken _ct = CancellationToken.None;

        // Every slot passed to AddRangeAsync, across all schedules
        private readonly List<AppointmentSlot> _allSlots = [];

        public GenerateUpcomingSlotsAsyncTests()
        {
            _uow.Slots.AddRangeAsync(
                    Arg.Do<IEnumerable<AppointmentSlot>>(slots => _allSlots.AddRange(slots)),
                    Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);
        }

        private static DoctorSchedule Schedule(int id, int doctorId, DayOfWeek day, int duration) => new()
        {
            Id = id,
            DoctorProfileId = doctorId,
            DayOfWeek = day,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(10, 0),
            SlotDurationMinutes = duration,
            IsActive = true
        };

        private static BranchHoliday Holiday(DateOnly from, DateOnly to, bool yearly = false, int? branchId = null)
            => new() { BranchId = branchId, From = from, To = to, RecursYearly = yearly };

        // The window starts on Sunday 18 Oct 2026, so its Mondays are the 19th and the 26th
        private static readonly DateOnly FirstMonday = new(2026, 10, 19);

        [Fact]
        public async Task GenerateUpcomingSlotsAsync_OnAHoliday_MakesNoSlots()
        {
            _uow.Doctors.GetActiveSchedulesAsync(_ct).Returns([Schedule(1, doctorId: 7, DayOfWeek.Monday, duration: 30)]);
            _uow.Branches.GetCalendarsAsync(_ct).Returns(new Dictionary<int, BranchCalendar>
            {
                [0] = OpenAllWeek(0, Holiday(FirstMonday, FirstMonday, branchId: 0))
            });

            await _sut.GenerateUpcomingSlotsAsync(_ct);

            Assert.Equal(2, _allSlots.Count);
            Assert.All(_allSlots, s => Assert.Equal(FirstMonday.AddDays(7), s.Date));
        }

        [Fact]
        public async Task GenerateUpcomingSlotsAsync_ARangeHolidayForEveryBranch_CoversEachDayOfIt()
        {
            _uow.Doctors.GetActiveSchedulesAsync(_ct).Returns(
            [
                Schedule(1, doctorId: 7, DayOfWeek.Monday, duration: 60),
                Schedule(2, doctorId: 8, DayOfWeek.Tuesday, duration: 60)
            ]);
            _uow.Branches.GetCalendarsAsync(_ct).Returns(new Dictionary<int, BranchCalendar>
            {
                [0] = OpenAllWeek(0, Holiday(FirstMonday.AddDays(7), FirstMonday.AddDays(8)))
            });

            await _sut.GenerateUpcomingSlotsAsync(_ct);

            Assert.Equal([FirstMonday, FirstMonday.AddDays(1)], _allSlots.Select(s => s.Date).Order());
        }

        [Fact]
        public async Task GenerateUpcomingSlotsAsync_AYearlyHolidayFromAnEarlierYear_StillApplies()
        {
            _uow.Doctors.GetActiveSchedulesAsync(_ct).Returns([Schedule(1, doctorId: 7, DayOfWeek.Monday, duration: 60)]);
            _uow.Branches.GetCalendarsAsync(_ct).Returns(new Dictionary<int, BranchCalendar>
            {
                [0] = OpenAllWeek(0, Holiday(new DateOnly(2019, 10, 26), new DateOnly(2019, 10, 26), yearly: true))
            });

            await _sut.GenerateUpcomingSlotsAsync(_ct);

            Assert.Equal(FirstMonday, Assert.Single(_allSlots).Date);
        }

        [Fact]
        public async Task GenerateUpcomingSlotsAsync_ASlotRunningPastClosingTime_IsNotMade()
        {
            // Schedule 09:00–10:00 in 30-minute slots; the branch closes at 09:45, so 09:30–10:00 doesn't fit
            _uow.Doctors.GetActiveSchedulesAsync(_ct).Returns([Schedule(1, doctorId: 7, DayOfWeek.Monday, duration: 30)]);
            _uow.Branches.GetCalendarsAsync(_ct).Returns(new Dictionary<int, BranchCalendar>
            {
                [0] = OpenDaily(0, (new TimeOnly(8, 0), new TimeOnly(9, 45)))
            });

            await _sut.GenerateUpcomingSlotsAsync(_ct);

            Assert.Equal(2, _allSlots.Count);
            Assert.All(_allSlots, s => Assert.Equal(new TimeOnly(9, 0), s.StartTime));
        }

        [Fact]
        public async Task GenerateUpcomingSlotsAsync_WithActiveSchedules_FillsTheWindowForEachAndReturnsTheCount()
        {
            _uow.Doctors.GetActiveSchedulesAsync(_ct).Returns(
            [
                Schedule(1, doctorId: 7, DayOfWeek.Monday, duration: 30),  // 2 slots a day
                Schedule(2, doctorId: 8, DayOfWeek.Tuesday, duration: 60)  // 1 slot a day
            ]);

            var created = await _sut.GenerateUpcomingSlotsAsync(_ct);

            // A 14-day window holds each weekday twice
            Assert.Equal(2 * 2 + 2 * 1, created);
            Assert.Equal(created, _allSlots.Count);
            Assert.All(_allSlots, s => Assert.InRange(s.Date, ClinicToday, ClinicToday.AddDays(13)));
            Assert.Equal(4, _allSlots.Count(s => s.DoctorScheduleId == 1 && s.Date.DayOfWeek == DayOfWeek.Monday));
            Assert.Equal(2, _allSlots.Count(s => s.DoctorScheduleId == 2 && s.Date.DayOfWeek == DayOfWeek.Tuesday));
        }

        [Fact]
        public async Task GenerateUpcomingSlotsAsync_WhenSlotsAlreadyExist_OnlyCreatesTheMissingOnes()
        {
            // Yesterday's run created the first week; today only the new days are added
            var schedule = Schedule(1, doctorId: 7, DayOfWeek.Monday, duration: 30);
            var firstMonday = Enumerable.Range(0, 7).Select(ClinicToday.AddDays).First(d => d.DayOfWeek == DayOfWeek.Monday);
            _uow.Doctors.GetActiveSchedulesAsync(_ct).Returns([schedule]);
            _uow.Slots.GetSlotTimesAsync(7, ClinicToday, ClinicToday.AddDays(13), _ct)
                .Returns(new List<(DateOnly, TimeOnly, TimeOnly)>
                {
                    (firstMonday, new TimeOnly(9, 0), new TimeOnly(9, 30)),
                    (firstMonday, new TimeOnly(9, 30), new TimeOnly(10, 0))
                });

            var created = await _sut.GenerateUpcomingSlotsAsync(_ct);

            Assert.Equal(2, created);
            Assert.All(_allSlots, s => Assert.Equal(firstMonday.AddDays(7), s.Date));
        }

        [Fact]
        public async Task GenerateUpcomingSlotsAsync_WhenNoActiveSchedules_CreatesNothing()
        {
            _uow.Doctors.GetActiveSchedulesAsync(_ct).Returns([]);

            var created = await _sut.GenerateUpcomingSlotsAsync(_ct);

            Assert.Equal(0, created);
            await _uow.Slots.DidNotReceive().AddRangeAsync(Arg.Any<IEnumerable<AppointmentSlot>>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GenerateUpcomingSlotsAsync_WhenASaveFails_StopsAndReturnsWhatWasCreated()
        {
            // The failed slots would stay in the change tracker and fail every later save, so the run stops
            _uow.Doctors.GetActiveSchedulesAsync(_ct).Returns(
            [
                Schedule(1, doctorId: 7, DayOfWeek.Monday, duration: 30),
                Schedule(2, doctorId: 8, DayOfWeek.Tuesday, duration: 30),
                Schedule(3, doctorId: 9, DayOfWeek.Wednesday, duration: 30)
            ]);
            _uow.SaveChangesAsync(_ct).Returns(
                _ => Task.FromResult(4),
                _ => throw new DbUpdateException("duplicate key"));

            var created = await _sut.GenerateUpcomingSlotsAsync(_ct);

            Assert.Equal(4, created);
            await _uow.Slots.DidNotReceive().GetSlotTimesAsync(9, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
        }
    }
}
