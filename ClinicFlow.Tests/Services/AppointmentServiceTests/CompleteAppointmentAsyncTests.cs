using Domain.Entities.AppModule;
using Domain.Enums;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shared.DTOs.Notification;
using Shared.Errors;

namespace ClinicFlow.Tests.Services.AppointmentServiceTests
{
    public class CompleteAppointmentAsyncTests : AppointmentServiceTestsBase
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

            // The visit started at 10:00 and "now" is 10:30, so it can be completed
            _dateTimeProvider.ClinicNow.Returns(new DateTime(2026, 10, 18, 10, 30, 0, DateTimeKind.Utc));
            _appointment = new Appointment
            {
                Id = _appointmentId,
                DoctorProfileId = _doctorId,
                Status = AppointmentStatus.Confirmed,
                Patient = new PatientProfile { UserId = _patientUserId },
                Slot = new AppointmentSlot { Date = new DateOnly(2026, 10, 18), StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(10, 30) }
            };
            _uow.Appointments.GetByIdAsync(_appointmentId, _ct).Returns(_appointment);
        }

        [Fact]
        public async Task CompleteAppointmentAsync_WhenAppointmentHasNotStarted_ReturnsCannotCompleteBeforeStartAndChangesNothing()
        {
            // REVIEW.md M14: completing early would unlock a review for a visit that never happened
            _dateTimeProvider.ClinicNow.Returns(new DateTime(2026, 10, 18, 9, 59, 0, DateTimeKind.Utc));

            var result = await _sut.CompleteAppointmentAsync(_doctorUserId, _appointmentId, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AppointmentErrors.CannotCompleteBeforeStart, result.Errors);
            Assert.Equal(AppointmentStatus.Confirmed, _appointment.Status);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
            await _notificationService.DidNotReceive().CreateRangeAsync(Arg.Any<IEnumerable<CreateNotificationRequest>>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task CompleteAppointmentAsync_WhenAppointmentStartsNow_Succeeds()
        {
            _dateTimeProvider.ClinicNow.Returns(new DateTime(2026, 10, 18, 10, 0, 0, DateTimeKind.Utc));

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
        public async Task CompleteAppointmentAsync_WhenValid_NotifiesPatientOnly()
        {
            List<CreateNotificationRequest>? sent = null;
            await _notificationService.CreateRangeAsync(
                Arg.Do<IEnumerable<CreateNotificationRequest>>(n => sent = n.ToList()),
                Arg.Any<CancellationToken>());

            await _sut.CompleteAppointmentAsync(_doctorUserId, _appointmentId, _ct);

            var notification = Assert.Single(sent!);
            Assert.Equal(_patientUserId, notification.UserId);
            Assert.Equal(NotificationType.AppointmentCompleted, notification.Type);
            Assert.Equal(_appointmentId, notification.RelatedEntityId);
        }

        [Fact]
        public async Task CompleteAppointmentAsync_WhenNotificationFails_StillReturnsSuccess()
        {
            _notificationService
                .CreateRangeAsync(Arg.Any<IEnumerable<CreateNotificationRequest>>(), Arg.Any<CancellationToken>())
                .Throws(new InvalidOperationException("notification store down"));

            var result = await _sut.CompleteAppointmentAsync(_doctorUserId, _appointmentId, _ct);

            Assert.True(result.IsSuccess);
            await _uow.Received(1).SaveChangesAsync(_ct);
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
            await _notificationService.DidNotReceive()
                .CreateRangeAsync(Arg.Any<IEnumerable<CreateNotificationRequest>>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task CompleteAppointmentAsync_WhenCancelled_ReturnsCannotCompleteCancelledError()
        {
            _appointment.Status = AppointmentStatus.Cancelled;

            var result = await _sut.CompleteAppointmentAsync(_doctorUserId, _appointmentId, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AppointmentErrors.CannotCompleteCancelled, result.Errors);
            Assert.Equal(AppointmentStatus.Cancelled, _appointment.Status);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task CompleteAppointmentAsync_WhenAlreadyCompleted_ReturnsAlreadyCompletedError()
        {
            _appointment.Status = AppointmentStatus.Completed;

            var result = await _sut.CompleteAppointmentAsync(_doctorUserId, _appointmentId, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AppointmentErrors.AlreadyCompleted, result.Errors);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
            await _notificationService.DidNotReceive()
                .CreateRangeAsync(Arg.Any<IEnumerable<CreateNotificationRequest>>(), Arg.Any<CancellationToken>());
        }
    }
}
