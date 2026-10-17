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
        public async Task DeleteScheduleAsync_WhenCallerOwnsSchedule_DeletesAndSaves()
        {
            var result = await _sut.DeleteScheduleAsync(_scheduleId, _ownerUserId, _ct);

            Assert.True(result.IsSuccess);
            _uow.Doctors.Received(1).DeleteSchedule(_schedule);
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
