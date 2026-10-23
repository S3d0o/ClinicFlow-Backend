using Domain.Entities.AppModule;
using NSubstitute;
using Shared.DTOs.Doctor;

namespace ClinicFlow.Tests.Services.DoctorServiceTests
{
    public class CreateScheduleAsyncTests : DoctorServiceTestsBase
    {
        private readonly Guid _doctorUserId = Guid.NewGuid();
        private readonly CancellationToken _ct = CancellationToken.None;

        // Any 14-day window contains each weekday exactly twice
        private const int MatchingDaysInWindow = 2;

        public CreateScheduleAsyncTests()
        {
            _uow.Doctors.GetByUserIdAsync(_doctorUserId, _ct).Returns(new DoctorProfile { Id = 7, UserId = _doctorUserId });
            _uow.Doctors.GetScheduleByUserIdAndDayAsync(_doctorUserId, Arg.Any<DayOfWeek>(), _ct).Returns((DoctorSchedule?)null);
        }

        private static CreateScheduleRequest Request(string start, string end, int duration) => new()
        {
            DayOfWeek = DayOfWeek.Monday,
            StartTime = TimeOnly.Parse(start),
            EndTime = TimeOnly.Parse(end),
            SlotDurationMinutes = duration
        };

        [Fact]
        public async Task CreateScheduleAsync_WhenScheduleEndsNearMidnight_StopsWithoutWrappingPastMidnight()
        {
            // 22:30 + 60 min = 23:30; the next slot (23:30–00:30) would wrap, so only one slot per day
            var result = await _sut.CreateScheduleAsync(_doctorUserId, Request("22:30", "23:59", 60), _ct);

            Assert.True(result.IsSuccess);
            Assert.NotNull(_generatedSlots);
            Assert.Equal(MatchingDaysInWindow, _generatedSlots!.Count);
            Assert.All(_generatedSlots, s =>
            {
                Assert.Equal(new TimeOnly(22, 30), s.StartTime);
                Assert.Equal(new TimeOnly(23, 30), s.EndTime);
            });
        }

        [Fact]
        public async Task CreateScheduleAsync_WhenWindowIsNotAMultipleOfDuration_LastSlotDoesNotOverrunEndTime()
        {
            // 09:00–09:45 with 30-minute slots: only 09:00–09:30 fits
            await _sut.CreateScheduleAsync(_doctorUserId, Request("09:00", "09:45", 30), _ct);

            Assert.NotNull(_generatedSlots);
            Assert.Equal(MatchingDaysInWindow, _generatedSlots!.Count);
            Assert.All(_generatedSlots, s => Assert.True(s.EndTime <= new TimeOnly(9, 45)));
        }

        [Fact]
        public async Task CreateScheduleAsync_WhenWindowIsExactMultiple_GeneratesAllSlots()
        {
            // 09:00–11:00 with 30-minute slots: 4 slots per matching day
            await _sut.CreateScheduleAsync(_doctorUserId, Request("09:00", "11:00", 30), _ct);

            Assert.NotNull(_generatedSlots);
            Assert.Equal(4 * MatchingDaysInWindow, _generatedSlots!.Count);
            Assert.All(_generatedSlots, s => Assert.Equal(DayOfWeek.Monday, s.Date.DayOfWeek));
        }

        [Fact]
        public async Task CreateScheduleAsync_WhenValid_PutsTheScheduleAndItsSlotsInTheDefaultBranch()
        {
            // One branch until P1 lets staff choose; slots inherit their schedule's branch (docs/v2/adr/0001)
            DoctorSchedule? added = null;
            _uow.Doctors.AddSchedule(Arg.Do<DoctorSchedule>(s => added = s));

            await _sut.CreateScheduleAsync(_doctorUserId, Request("09:00", "10:00", 30), _ct);

            Assert.Equal(DefaultBranchId, added!.BranchId);
            Assert.NotEmpty(_generatedSlots!);
            Assert.All(_generatedSlots!, s => Assert.Equal(DefaultBranchId, s.BranchId));
        }

        [Fact]
        public async Task CreateScheduleAsync_WhenValid_StoresEachSlotsUtcInstants()
        {
            // Cairo is still on daylight saving (UTC+3) on Monday 19 Oct 2026
            await _sut.CreateScheduleAsync(_doctorUserId, Request("09:00", "09:30", 30), _ct);

            var slot = _generatedSlots!.Single(s => s.Date == new DateOnly(2026, 10, 19));
            Assert.Equal(new DateTime(2026, 10, 19, 6, 0, 0, DateTimeKind.Utc), slot.StartsAtUtc);
            Assert.Equal(new DateTime(2026, 10, 19, 6, 30, 0, DateTimeKind.Utc), slot.EndsAtUtc);
            Assert.Equal(DateTimeKind.Utc, slot.StartsAtUtc.Kind);
        }

        [Fact]
        public async Task CreateScheduleAsync_OnTheNightTheClocksGoBack_UsesTheFirstOccurrenceOfTheRepeatedHour()
        {
            // Thursday 29 Oct 2026: Cairo goes from UTC+3 back to UTC+2 at midnight, so 23:00–24:00 happens twice
            var thursday = new CreateScheduleRequest
            {
                DayOfWeek = DayOfWeek.Thursday, StartTime = new TimeOnly(22, 0), EndTime = new TimeOnly(23, 59), SlotDurationMinutes = 30
            };

            await _sut.CreateScheduleAsync(_doctorUserId, thursday, _ct);

            var night = _generatedSlots!.Where(s => s.Date == new DateOnly(2026, 10, 29)).OrderBy(s => s.StartTime).ToList();
            Assert.Equal([new TimeOnly(22, 0), new TimeOnly(22, 30), new TimeOnly(23, 0)], night.Select(s => s.StartTime));
            Assert.Equal(new DateTime(2026, 10, 29, 20, 0, 0, DateTimeKind.Utc), night[2].StartsAtUtc); // 23:00 at UTC+3
            // Back to back, in order, with no overlap
            Assert.All(night.Zip(night.Skip(1)), pair => Assert.Equal(pair.First.EndsAtUtc, pair.Second.StartsAtUtc));
        }

        [Fact]
        public async Task CreateScheduleAsync_OnTheNightTheClocksGoForward_SkipsSlotsInTheMissingHour()
        {
            // Friday 24 Apr 2026: Cairo jumps from 00:00 to 01:00, so 00:00–01:00 never happens that night
            _clock.Today(BranchZone).Returns(new DateOnly(2026, 4, 20));
            var friday = new CreateScheduleRequest
            {
                DayOfWeek = DayOfWeek.Friday, StartTime = new TimeOnly(0, 0), EndTime = new TimeOnly(2, 0), SlotDurationMinutes = 30
            };

            await _sut.CreateScheduleAsync(_doctorUserId, friday, _ct);

            var night = _generatedSlots!.Where(s => s.Date == new DateOnly(2026, 4, 24)).OrderBy(s => s.StartTime).ToList();
            Assert.Equal([new TimeOnly(1, 0), new TimeOnly(1, 30)], night.Select(s => s.StartTime));
            Assert.Equal(new DateTime(2026, 4, 23, 22, 0, 0, DateTimeKind.Utc), night[0].StartsAtUtc); // 01:00 at UTC+3
            Assert.Equal(4, _generatedSlots!.Count(s => s.Date == new DateOnly(2026, 5, 1))); // an ordinary Friday
        }

        [Fact]
        public async Task CreateScheduleAsync_WhenActiveScheduleExistsForTheDay_ReturnsScheduleDayAlreadyExists()
        {
            _uow.Doctors.GetScheduleByUserIdAndDayAsync(_doctorUserId, DayOfWeek.Monday, _ct)
                .Returns(new DoctorSchedule { Id = 3, DoctorProfileId = 7, DayOfWeek = DayOfWeek.Monday, IsActive = true });

            var result = await _sut.CreateScheduleAsync(_doctorUserId, Request("09:00", "12:00", 30), _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(Shared.Errors.DoctorErrors.ScheduleDayAlreadyExists, result.Errors);
            Assert.Null(_generatedSlots);
        }

        [Fact]
        public async Task CreateScheduleAsync_WhenInactiveScheduleExistsForTheDay_ReactivatesItWithTheNewHours()
        {
            // REVIEW.md H4: a schedule kept for its history when deleted is reused for that weekday
            var inactive = new DoctorSchedule
            {
                Id = 3, DoctorProfileId = 7, DayOfWeek = DayOfWeek.Monday, IsActive = false,
                StartTime = new TimeOnly(14, 0), EndTime = new TimeOnly(18, 0), SlotDurationMinutes = 60
            };
            _uow.Doctors.GetScheduleByUserIdAndDayAsync(_doctorUserId, DayOfWeek.Monday, _ct).Returns(inactive);

            var result = await _sut.CreateScheduleAsync(_doctorUserId, Request("09:00", "10:00", 30), _ct);

            Assert.True(result.IsSuccess);
            Assert.Equal(3, result.Value.Id);
            Assert.True(inactive.IsActive);
            Assert.Equal(new TimeOnly(9, 0), inactive.StartTime);
            Assert.Equal(new TimeOnly(10, 0), inactive.EndTime);
            Assert.Equal(30, inactive.SlotDurationMinutes);
            // Blocked slots of cancelled appointments that fit the new hours are reopened
            await _uow.Slots.Received(1).SyncKeptSlotsWithScheduleAsync(inactive, ClinicToday, _ct);
            _uow.Doctors.DidNotReceive().AddSchedule(Arg.Any<DoctorSchedule>());
            Assert.Equal(2 * MatchingDaysInWindow, _generatedSlots!.Count);
        }

        [Fact]
        public async Task CreateScheduleAsync_WhenDoctorAlreadyHasOverlappingSlots_SkipsThoseTimes()
        {
            // REVIEW.md H4: an existing (e.g. booked) 09:15–09:45 slot blocks both 09:00–09:30 and 09:30–10:00 that day
            var today = ClinicToday;
            var firstMonday = Enumerable.Range(0, 7).Select(today.AddDays).First(d => d.DayOfWeek == DayOfWeek.Monday);
            _uow.Slots.GetSlotTimesAsync(7, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), _ct)
                .Returns(new List<(DateOnly, TimeOnly, TimeOnly)> { (firstMonday, new TimeOnly(9, 15), new TimeOnly(9, 45)) });

            await _sut.CreateScheduleAsync(_doctorUserId, Request("09:00", "10:30", 30), _ct);

            Assert.DoesNotContain(_generatedSlots!, s => s.Date == firstMonday && s.StartTime < new TimeOnly(10, 0));
            Assert.Contains(_generatedSlots!, s => s.Date == firstMonday && s.StartTime == new TimeOnly(10, 0));
            Assert.Equal(3 * MatchingDaysInWindow - 2, _generatedSlots!.Count);
        }
    }
}
