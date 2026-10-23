using ClinicFlow.Domain.Enums;
using Domain.Entities.AppModule;
using Domain.Entities.IdentityModule;
using Domain.Enums;
using NSubstitute;
using Shared.DTOs.Appointment;
using Domain.Events;
using Shared.Errors;
using Domain.Errors;

namespace ClinicFlow.Tests.Services.AppointmentCancellationServiceTests
{
    public class CancelByAdminAsyncTests : AppointmentCancellationServiceTestsBase
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

            // Starts in 30 minutes: admins are not bound by the patient's 2-hour window
            var slotStart = _now.AddMinutes(30);
            _appointment = new Appointment
            {
                Id = _appointmentId,
                Slot = new AppointmentSlot
                {
                    Id = 1,
                    Date = DateOnly.FromDateTime(slotStart),
                    StartTime = TimeOnly.FromDateTime(slotStart),
                    StartsAtUtc = UtcNow.AddMinutes(30),
                    Status = SlotStatus.Booked,
                    DoctorSchedule = new DoctorSchedule { StartTime = TimeOnly.MinValue, EndTime = TimeOnly.MaxValue }
                },
                Doctor = new DoctorProfile { UserId = _doctorUserId },
                Patient = new PatientProfile
                {
                    UserId = _patientUserId,
                    User = new ApplicationUser { FirstName = "Saad", LastName = "Mohamed" }
                }
            }.WithStatus(AppointmentStatus.Confirmed);
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
            Assert.Equal(UtcNow, _appointment.CancelledAt);
            Assert.Equal(SlotStatus.Available, _appointment.Slot.Status);
            await _uow.Received(1).SaveChangesAsync(_ct);
        }

        [Fact]
        public async Task CancelByAdminAsync_WhenScheduleDeactivated_BlocksSlot()
        {
            // REVIEW.md H4: a freed slot of a deactivated schedule must not become bookable
            _appointment.Slot.DoctorSchedule.IsActive = false;

            var result = await _sut.CancelByAdminAsync(_appointmentId, _request, _ct);

            Assert.True(result.IsSuccess);
            Assert.Equal(SlotStatus.Blocked, _appointment.Slot.Status);
        }

        [Fact]
        public async Task CancelByAdminAsync_WhenValid_RaisesTheEventThatNotifiesBoth()
        {
            // Saved to the outbox with the change; AppointmentCancelledHandler notifies the patient and the doctor
            await _sut.CancelByAdminAsync(_appointmentId, _request, _ct);

            Assert.Equal([new AppointmentCancelled(_appointmentId, CancelledBy.Admin)], _appointment.TakeDomainEvents());
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
            _appointment.WithStatus(AppointmentStatus.Cancelled);

            var result = await _sut.CancelByAdminAsync(_appointmentId, _request, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AppointmentErrors.AlreadyCancelled, result.Errors);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task CancelByAdminAsync_WhenCompleted_ReturnsAlreadyCompletedErrorAndKeepsStatus()
        {
            // REVIEW.md M14: admins used to get AlreadyCancelled for a completed appointment
            _appointment.WithStatus(AppointmentStatus.Completed);

            var result = await _sut.CancelByAdminAsync(_appointmentId, _request, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AppointmentErrors.AlreadyCompleted, result.Errors);
            Assert.DoesNotContain(AppointmentErrors.AlreadyCancelled, result.Errors);
            Assert.Equal(AppointmentStatus.Completed, _appointment.Status);
            Assert.Equal(SlotStatus.Booked, _appointment.Slot.Status);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task CancelByAdminAsync_WhenInProgress_ReturnsInvalidStatusTransitionAndKeepsTheSlot()
        {
            // A visit that has started can't be cancelled (05 §4.1)
            _appointment.WithStatus(AppointmentStatus.InProgress);

            var result = await _sut.CancelByAdminAsync(_appointmentId, _request, _ct);

            Assert.Contains(AppointmentErrors.InvalidStatusTransition(AppointmentStatus.InProgress, AppointmentStatus.Cancelled), result.Errors);
            Assert.Equal(SlotStatus.Booked, _appointment.Slot.Status);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }
}
