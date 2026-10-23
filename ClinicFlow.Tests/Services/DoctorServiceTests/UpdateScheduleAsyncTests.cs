using Domain.Entities.AppModule;
using Domain.Interfaces;
using NSubstitute;
using Shared.DTOs.Doctor;
using Shared.Errors;

namespace ClinicFlow.Tests.Services.DoctorServiceTests
{
    public class UpdateScheduleAsyncTests : DoctorServiceTestsBase
    {
        private const int _scheduleId = 3;
        private readonly Guid _doctorUserId = Guid.NewGuid();
        private readonly CancellationToken _ct = CancellationToken.None;
        private readonly DoctorSchedule _schedule;

        public UpdateScheduleAsyncTests()
        {
            _schedule = new DoctorSchedule
            {
                Id = _scheduleId,
                DoctorProfileId = 7,
                DayOfWeek = DayOfWeek.Monday,
                StartTime = new TimeOnly(9, 0),
                EndTime = new TimeOnly(17, 0),
                SlotDurationMinutes = 30,
                IsActive = true,
                DoctorProfile = new DoctorProfile { Id = 7, UserId = _doctorUserId }
            };
            _uow.Doctors.GetScheduleByIdAsync(_scheduleId, _ct).Returns(_schedule);
        }

        [Fact]
        public async Task UpdateScheduleAsync_WhenValid_SavesAndRegeneratesSlotsInOneTransaction()
        {
            // A failure can't leave the schedule changed but its slots half-replaced (REVIEW.md H4)
            var result = await _sut.UpdateScheduleAsync(_scheduleId, _doctorUserId,
                new UpdateScheduleRequest { StartTime = new TimeOnly(10, 0) }, _ct);

            Assert.True(result.IsSuccess);
            Received.InOrder(() =>
            {
                _uow.ExecuteInTransactionAsync(Arg.Any<Func<ITransaction, Task<int>>>(), _ct);
                _uow.SaveChangesAsync(_ct);
                _uow.Slots.DeleteFutureAvailableByScheduleAsync(_scheduleId, ClinicToday, _ct);
                _uow.Slots.SyncKeptSlotsWithScheduleAsync(_schedule, ClinicToday, _ct);
                _uow.Slots.AddRangeAsync(Arg.Any<IEnumerable<AppointmentSlot>>(), _ct);
                _uow.SaveChangesAsync(_ct);
                _transaction.CommitAsync(_ct);
            });
        }

        [Fact]
        public async Task UpdateScheduleAsync_WhenInvalid_OpensNoTransaction()
        {
            var result = await _sut.UpdateScheduleAsync(_scheduleId, _doctorUserId,
                new UpdateScheduleRequest { EndTime = new TimeOnly(8, 0) }, _ct);

            Assert.True(result.IsFailure);
            await _uow.DidNotReceiveWithAnyArgs().ExecuteInTransactionAsync<int>(default!, default);
        }

        [Fact]
        public async Task UpdateScheduleAsync_WhenOnlyStartTimeSent_KeepsOtherFields()
        {
            var result = await _sut.UpdateScheduleAsync(_scheduleId, _doctorUserId,
                new UpdateScheduleRequest { StartTime = new TimeOnly(10, 0) }, _ct);

            Assert.True(result.IsSuccess);
            Assert.Equal(new TimeOnly(10, 0), _schedule.StartTime);
            Assert.Equal(new TimeOnly(17, 0), _schedule.EndTime);
            Assert.Equal(30, _schedule.SlotDurationMinutes);
            Assert.True(_schedule.IsActive);
            Assert.NotNull(_generatedSlots);
            Assert.NotEmpty(_generatedSlots!);
        }

        [Fact]
        public async Task UpdateScheduleAsync_WhenMergedEndTimeIsBeforeStartTime_ReturnsInvalidScheduleTime()
        {
            var result = await _sut.UpdateScheduleAsync(_scheduleId, _doctorUserId,
                new UpdateScheduleRequest { EndTime = new TimeOnly(8, 0) }, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(DoctorErrors.InvalidScheduleTime, result.Errors);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task UpdateScheduleAsync_WhenDeactivated_RemovesFutureSlotsAndGeneratesNone()
        {
            var result = await _sut.UpdateScheduleAsync(_scheduleId, _doctorUserId,
                new UpdateScheduleRequest { IsActive = false }, _ct);

            Assert.True(result.IsSuccess);
            Assert.False(_schedule.IsActive);
            await _uow.Slots.Received(1).DeleteFutureAvailableByScheduleAsync(_scheduleId, ClinicToday, _ct);
            // Kept free slots (cancelled appointments point to them) are blocked, not left bookable (REVIEW.md H4)
            await _uow.Slots.Received(1).SyncKeptSlotsWithScheduleAsync(_schedule, ClinicToday, _ct);
            Assert.Null(_generatedSlots);
        }

        [Fact]
        public async Task UpdateScheduleAsync_WhenScheduleBelongsToAnotherDoctor_ReturnsNotFound()
        {
            var result = await _sut.UpdateScheduleAsync(_scheduleId, Guid.NewGuid(),
                new UpdateScheduleRequest { StartTime = new TimeOnly(10, 0) }, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(DoctorErrors.ScheduleNotFound(_scheduleId), result.Errors);
        }
    }
}
