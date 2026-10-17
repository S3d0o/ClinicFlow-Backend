using Domain.Entities.AppModule;
using NSubstitute;
using Shared.Errors;

namespace ClinicFlow.Tests.Services.DoctorServiceTests
{
    public class DeleteScheduleAsyncTests : DoctorServiceTestsBase
    {
        private const int _scheduleId = 4;
        private readonly Guid _ownerUserId = Guid.NewGuid();
        private readonly CancellationToken _ct = new CancellationTokenSource().Token;
        private readonly DoctorSchedule _schedule;

        public DeleteScheduleAsyncTests()
        {
            _schedule = new DoctorSchedule
            {
                Id = _scheduleId,
                DoctorProfileId = 2,
                DoctorProfile = new DoctorProfile { Id = 2, UserId = _ownerUserId }
            };
            _uow.Doctors.GetScheduleByIdAsync(_scheduleId, _ct).Returns(_schedule);
        }

        [Fact]
        public async Task DeleteScheduleAsync_WhenCallerOwnsScheduleWithoutHistory_DeletesItAndItsSlots()
        {
            var result = await _sut.DeleteScheduleAsync(_scheduleId, _ownerUserId, _ct);

            Assert.True(result.IsSuccess);
            await _uow.Slots.Received(1).DeleteUnusedByScheduleAsync(_scheduleId, _ct);
            _uow.Doctors.Received(1).DeleteSchedule(_schedule);
            await _uow.Received(1).SaveChangesAsync(_ct);
        }

        [Fact]
        public async Task DeleteScheduleAsync_WhenUpcomingAppointmentsAreBooked_ReturnsConflictAndChangesNothing()
        {
            // REVIEW.md H4: the doctor must cancel upcoming appointments before removing the schedule
            _uow.Slots.HasUpcomingBookingsAsync(_scheduleId, _ct).Returns(true);

            var result = await _sut.DeleteScheduleAsync(_scheduleId, _ownerUserId, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(DoctorErrors.CannotDeleteScheduleWithBookedSlots, result.Errors);
            await _uow.Slots.DidNotReceive().DeleteUnusedByScheduleAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
            _uow.Doctors.DidNotReceive().DeleteSchedule(Arg.Any<DoctorSchedule>());
            Assert.True(_schedule.IsActive);
        }

        [Fact]
        public async Task DeleteScheduleAsync_WhenPastAppointmentsUseItsSlots_DeactivatesInsteadOfDeleting()
        {
            // REVIEW.md H4: slots with appointment history can't be deleted (NoAction FK), so the history is kept
            _uow.Slots.AnyForScheduleAsync(_scheduleId, _ct).Returns(true);

            var result = await _sut.DeleteScheduleAsync(_scheduleId, _ownerUserId, _ct);

            Assert.True(result.IsSuccess);
            Assert.False(_schedule.IsActive);
            await _uow.Slots.Received(1).DeleteUnusedByScheduleAsync(_scheduleId, _ct);
            _uow.Doctors.DidNotReceive().DeleteSchedule(Arg.Any<DoctorSchedule>());
            await _uow.Received(1).SaveChangesAsync(_ct);
        }

        [Fact]
        public async Task DeleteScheduleAsync_WhenScheduleNotFound_ReturnsScheduleNotFoundError()
        {
            var result = await _sut.DeleteScheduleAsync(999, _ownerUserId, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(DoctorErrors.ScheduleNotFound(999), result.Errors);
            _uow.Doctors.DidNotReceive().DeleteSchedule(Arg.Any<DoctorSchedule>());
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task DeleteScheduleAsync_WhenScheduleBelongsToAnotherDoctor_ReturnsScheduleNotFoundAndDeletesNothing()
        {
            // Same 404 as an unknown id, so doctors can't probe other doctors' schedule ids (contract #17)
            var result = await _sut.DeleteScheduleAsync(_scheduleId, Guid.NewGuid(), _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(DoctorErrors.ScheduleNotFound(_scheduleId), result.Errors);
            _uow.Doctors.DidNotReceive().DeleteSchedule(Arg.Any<DoctorSchedule>());
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }
}
