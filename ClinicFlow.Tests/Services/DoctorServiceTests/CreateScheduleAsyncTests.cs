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
    }
}
