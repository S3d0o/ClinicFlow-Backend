using ClinicFlow.Domain.Enums;
using Domain.Entities.AppModule;
using Domain.Entities.IdentityModule;
using Domain.Enums;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shared.DTOs.Appointment;
using Shared.DTOs.Notification;
using Shared.Errors;

namespace ClinicFlow.Tests.Services.AppointmentServiceTests
{
    public class CancelByAdminAsyncTests : AppointmentServiceTestsBase
    {
        private const int _appointmentId = 1;
        private readonly Guid _doctorUserId = Guid.NewGuid();
        private readonly Guid _patientUserId = Guid.NewGuid();
        private readonly CancellationToken _ct = CancellationToken.None;
        private readonly CancelAppointmentRequest _request = new("Clinic closed");
        private readonly DateTime _now = new(2026, 6, 15, 10, 0, 0);
        private readonly Appointment _appointment;

        public CancelByAdminAsyncTests()
        {
            _dateTimeProvider.UtcNow.Returns(_now);

            // Starts in 30 minutes: admins are not bound by the patient's 2-hour window
            var slotStart = _now.AddMinutes(30);
            _appointment = new Appointment
            {
                Id = _appointmentId,
                Status = AppointmentStatus.Confirmed,
                Slot = new AppointmentSlot
                {
                    Id = 1,
                    Date = DateOnly.FromDateTime(slotStart),
                    StartTime = TimeOnly.FromDateTime(slotStart),
                    Status = SlotStatus.Booked
                },
                Doctor = new DoctorProfile { UserId = _doctorUserId },
                Patient = new PatientProfile
                {
                    UserId = _patientUserId,
                    User = new ApplicationUser { FirstName = "Saad", LastName = "Mohamed" }
                }
            };
            _uow.Appointments.GetByIdAsync(_appointmentId, _ct).Returns(_appointment);
        }

        [Fact]
        public async Task CancelByAdminAsync_WhenValid_CancelsAppointmentAndFreesSlot()
        {
            var result = await _sut.CancelByAdminAsync(_appointmentId, _request, _ct);

            Assert.True(result.IsSuccess);
            Assert.Equal(AppointmentStatus.Cancelled, _appointment.Status);
            Assert.Equal(CancelledBy.Admin, _appointment.CancelledBy);
            Assert.Equal("Clinic closed", _appointment.CancellationReason);
            Assert.NotNull(_appointment.CancelledAt);
            Assert.Equal(SlotStatus.Available, _appointment.Slot.Status);
            await _uow.Received(1).SaveChangesAsync(_ct);
        }

        [Fact]
        public async Task CancelByAdminAsync_WhenValid_NotifiesPatientAndDoctor()
        {
            List<CreateNotificationRequest>? sent = null;
            await _notificationService.CreateRangeAsync(
                Arg.Do<IEnumerable<CreateNotificationRequest>>(n => sent = n.ToList()),
                Arg.Any<CancellationToken>());

            await _sut.CancelByAdminAsync(_appointmentId, _request, _ct);

            Assert.NotNull(sent);
            Assert.Equal([_patientUserId, _doctorUserId], sent!.Select(n => n.UserId));
            Assert.All(sent, n => Assert.Equal(NotificationType.AppointmentCancelled, n.Type));
        }

        [Fact]
        public async Task CancelByAdminAsync_WhenNotificationFails_StillReturnsSuccess()
        {
            _notificationService
                .CreateRangeAsync(Arg.Any<IEnumerable<CreateNotificationRequest>>(), Arg.Any<CancellationToken>())
                .Throws(new InvalidOperationException("notification store down"));

            var result = await _sut.CancelByAdminAsync(_appointmentId, _request, _ct);

            Assert.True(result.IsSuccess);
            await _uow.Received(1).SaveChangesAsync(_ct);
        }

        [Fact]
        public async Task CancelByAdminAsync_WhenAppointmentNotFound_ReturnsNotFoundError()
        {
            _uow.Appointments.GetByIdAsync(999, _ct).Returns((Appointment?)null);

            var result = await _sut.CancelByAdminAsync(999, _request, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AppointmentErrors.NotFound(999), result.Errors);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task CancelByAdminAsync_WhenAlreadyCancelled_ReturnsAlreadyCancelledError()
        {
            _appointment.Status = AppointmentStatus.Cancelled;

            var result = await _sut.CancelByAdminAsync(_appointmentId, _request, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AppointmentErrors.AlreadyCancelled, result.Errors);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task CancelByAdminAsync_WhenCompleted_ReturnsAlreadyCompletedErrorAndKeepsStatus()
        {
            // REVIEW.md M14: admins used to get AlreadyCancelled for a completed appointment
            _appointment.Status = AppointmentStatus.Completed;

            var result = await _sut.CancelByAdminAsync(_appointmentId, _request, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AppointmentErrors.AlreadyCompleted, result.Errors);
            Assert.DoesNotContain(AppointmentErrors.AlreadyCancelled, result.Errors);
            Assert.Equal(AppointmentStatus.Completed, _appointment.Status);
            Assert.Equal(SlotStatus.Booked, _appointment.Slot.Status);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }
}
