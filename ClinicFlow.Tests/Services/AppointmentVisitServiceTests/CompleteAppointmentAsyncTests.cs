using Domain.Entities.AppModule;
using Domain.Enums;
using NSubstitute;
using Domain.Events;
using Shared.Errors;
using Domain.Errors;

namespace ClinicFlow.Tests.Services.AppointmentVisitServiceTests
{
    public class CompleteAppointmentAsyncTests : AppointmentVisitServiceTestsBase
    {
        private const int _appointmentId = 1;
        private const int _doctorId = 5;
        private readonly Guid _doctorUserId = Guid.NewGuid();
        private readonly Guid _patientUserId = Guid.NewGuid();
        private readonly CancellationToken _ct = CancellationToken.None;
        private readonly Appointment _appointment;

        public CompleteAppointmentAsyncTests()
        {
            _uow.Doctors.GetByUserIdAsync(_doctorUserId, _ct)
                .Returns(new DoctorProfile { Id = _doctorId, UserId = _doctorUserId });

            // The visit started at 10:00 in Cairo (07:00 UTC) and "now" is 07:30 UTC, so it can be completed
            _clock.UtcNow.Returns(new DateTime(2026, 10, 18, 7, 30, 0, DateTimeKind.Utc));
            _appointment = new Appointment
            {
                Id = _appointmentId,
                DoctorProfileId = _doctorId,
                Patient = new PatientProfile { UserId = _patientUserId },
                Slot = new AppointmentSlot
                {
                    Date = new DateOnly(2026, 10, 18), StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(10, 30),
                    StartsAtUtc = new DateTime(2026, 10, 18, 7, 0, 0, DateTimeKind.Utc)
                }
            }.WithStatus(AppointmentStatus.Confirmed);
            _uow.Appointments.GetByIdAsync(_appointmentId, _ct).Returns(_appointment);
        }

        [Fact]
        public async Task CompleteAppointmentAsync_WhenAppointmentHasNotStarted_ReturnsCannotCompleteBeforeStartAndChangesNothing()
        {
            // REVIEW.md M14: completing early would unlock a review for a visit that never happened
            _clock.UtcNow.Returns(new DateTime(2026, 10, 18, 6, 59, 0, DateTimeKind.Utc));

            var result = await _sut.CompleteAppointmentAsync(_doctorUserId, _appointmentId, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AppointmentErrors.CannotCompleteBeforeStart, result.Errors);
            Assert.Equal(AppointmentStatus.Confirmed, _appointment.Status);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
            Assert.Empty(_appointment.TakeDomainEvents());
        }

        [Fact]
        public async Task CompleteAppointmentAsync_WhenAppointmentStartsNow_Succeeds()
        {
            _clock.UtcNow.Returns(new DateTime(2026, 10, 18, 7, 0, 0, DateTimeKind.Utc));

            var result = await _sut.CompleteAppointmentAsync(_doctorUserId, _appointmentId, _ct);

            Assert.True(result.IsSuccess);
            Assert.Equal(AppointmentStatus.Completed, _appointment.Status);
        }

        [Fact]
        public async Task CompleteAppointmentAsync_WhenValid_MarksCompletedAndSaves()
        {
            var result = await _sut.CompleteAppointmentAsync(_doctorUserId, _appointmentId, _ct);

            Assert.True(result.IsSuccess);
            Assert.Equal(AppointmentStatus.Completed, _appointment.Status);
            await _uow.Received(1).SaveChangesAsync(_ct);
        }

        [Fact]
        public async Task CompleteAppointmentAsync_WhenValid_RaisesTheEventThatNotifiesThePatient()
        {
            // Saved to the outbox with the change; AppointmentCompletedHandler notifies the patient
            await _sut.CompleteAppointmentAsync(_doctorUserId, _appointmentId, _ct);

            Assert.Equal([new AppointmentCompleted(_appointmentId)], _appointment.TakeDomainEvents());
        }

        [Fact]
        public async Task CompleteAppointmentAsync_WhenDoctorProfileNotFound_ReturnsProfileNotFoundError()
        {
            var unknownUserId = Guid.NewGuid();
            _uow.Doctors.GetByUserIdAsync(unknownUserId, _ct).Returns((DoctorProfile?)null);

            var result = await _sut.CompleteAppointmentAsync(unknownUserId, _appointmentId, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(DoctorErrors.ProfileNotFound(unknownUserId), result.Errors);
            await _uow.Appointments.DidNotReceive().GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task CompleteAppointmentAsync_WhenAppointmentNotFound_ReturnsNotFoundError()
        {
            _uow.Appointments.GetByIdAsync(999, _ct).Returns((Appointment?)null);

            var result = await _sut.CompleteAppointmentAsync(_doctorUserId, 999, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AppointmentErrors.NotFound(999), result.Errors);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task CompleteAppointmentAsync_WhenAppointmentBelongsToAnotherDoctor_ReturnsForbiddenError()
        {
            _appointment.DoctorProfileId = _doctorId + 1;

            var result = await _sut.CompleteAppointmentAsync(_doctorUserId, _appointmentId, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AppointmentErrors.Forbidden, result.Errors);
            Assert.Equal(AppointmentStatus.Confirmed, _appointment.Status);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
            Assert.Empty(_appointment.TakeDomainEvents());
        }

        [Fact]
        public async Task CompleteAppointmentAsync_WhenCancelled_ReturnsCannotCompleteCancelledError()
        {
            _appointment.WithStatus(AppointmentStatus.Cancelled);

            var result = await _sut.CompleteAppointmentAsync(_doctorUserId, _appointmentId, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AppointmentErrors.CannotCompleteCancelled, result.Errors);
            Assert.Equal(AppointmentStatus.Cancelled, _appointment.Status);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task CompleteAppointmentAsync_WhenAlreadyCompleted_ReturnsAlreadyCompletedError()
        {
            _appointment.WithStatus(AppointmentStatus.Completed);

            var result = await _sut.CompleteAppointmentAsync(_doctorUserId, _appointmentId, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AppointmentErrors.AlreadyCompleted, result.Errors);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
            Assert.Empty(_appointment.TakeDomainEvents());
        }

        [Fact]
        public async Task CompleteAppointmentAsync_WhenCheckedInButNotStarted_ReturnsInvalidStatusTransition()
        {
            // Once a patient is checked in, the visit is completed from InProgress (05 §4.1)
            _appointment.WithStatus(AppointmentStatus.CheckedIn);

            var result = await _sut.CompleteAppointmentAsync(_doctorUserId, _appointmentId, _ct);

            Assert.Contains(AppointmentErrors.InvalidStatusTransition(AppointmentStatus.CheckedIn, AppointmentStatus.Completed), result.Errors);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task CompleteAppointmentAsync_WhenCompleted_RecordsTheTime()
        {
            await _sut.CompleteAppointmentAsync(_doctorUserId, _appointmentId, _ct);

            Assert.Equal(new DateTime(2026, 10, 18, 7, 30, 0, DateTimeKind.Utc), _appointment.CompletedAt);
        }
    }
}
