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
    public class CancelByDoctorAsyncTests : AppointmentServiceTestsBase
    {
        private const int _appointmentId = 1;
        private const int _doctorId = 5;
        private readonly Guid _doctorUserId = Guid.NewGuid();
        private readonly Guid _patientUserId = Guid.NewGuid();
        private readonly CancellationToken _ct = CancellationToken.None;
        private readonly CancelAppointmentRequest _request = new("Doctor unavailable");
        private readonly DateTime _now = new(2026, 6, 15, 10, 0, 0);
        private readonly Appointment _appointment;

        public CancelByDoctorAsyncTests()
        {
            _dateTimeProvider.ClinicNow.Returns(_now);
            _uow.Doctors.GetByUserIdAsync(_doctorUserId, _ct)
                .Returns(new DoctorProfile { Id = _doctorId, UserId = _doctorUserId });

            var slotStart = _now.AddDays(1);
            _appointment = new Appointment
            {
                Id = _appointmentId,
                DoctorProfileId = _doctorId,
                Status = AppointmentStatus.Confirmed,
                Slot = new AppointmentSlot
                {
                    Id = 1,
                    Date = DateOnly.FromDateTime(slotStart),
                    StartTime = TimeOnly.FromDateTime(slotStart),
                    Status = SlotStatus.Booked,
                    DoctorSchedule = new DoctorSchedule { StartTime = TimeOnly.MinValue, EndTime = TimeOnly.MaxValue }
                },
                Doctor = new DoctorProfile { Id = _doctorId, UserId = _doctorUserId },
                Patient = new PatientProfile
                {
                    UserId = _patientUserId,
                    User = new ApplicationUser { FirstName = "Saad", LastName = "Mohamed" }
                }
            };
            _uow.Appointments.GetByIdAsync(_appointmentId, _ct).Returns(_appointment);
        }

        [Fact]
        public async Task CancelByDoctorAsync_WhenValid_CancelsAppointmentAndFreesSlot()
        {
            var result = await _sut.CancelByDoctorAsync(_doctorUserId, _appointmentId, _request, _ct);

            Assert.True(result.IsSuccess);
            Assert.Equal(AppointmentStatus.Cancelled, _appointment.Status);
            Assert.Equal(CancelledBy.Doctor, _appointment.CancelledBy);
            Assert.Equal("Doctor unavailable", _appointment.CancellationReason);
            Assert.NotNull(_appointment.CancelledAt);
            Assert.Equal(SlotStatus.Available, _appointment.Slot.Status);
            await _uow.Received(1).SaveChangesAsync(_ct);
        }

        [Theory]
        [InlineData(true, 13, 17)]   // the doctor moved their hours away from the slot
        [InlineData(false, 0, 23)]   // the schedule was deactivated
        public async Task CancelByDoctorAsync_WhenScheduleNoLongerCoversSlot_BlocksSlot(bool isActive, int startHour, int endHour)
        {
            // REVIEW.md H4: a freed slot outside the doctor's current hours must not become bookable
            _appointment.Slot.StartTime = new TimeOnly(10, 0);
            _appointment.Slot.EndTime = new TimeOnly(10, 30);
            _appointment.Slot.DoctorSchedule = new DoctorSchedule
            {
                IsActive = isActive,
                StartTime = new TimeOnly(startHour, 0),
                EndTime = new TimeOnly(endHour, 0)
            };

            var result = await _sut.CancelByDoctorAsync(_doctorUserId, _appointmentId, _request, _ct);

            Assert.True(result.IsSuccess);
            Assert.Equal(AppointmentStatus.Cancelled, _appointment.Status);
            Assert.Equal(SlotStatus.Blocked, _appointment.Slot.Status);
        }

        [Fact]
        public async Task CancelByDoctorAsync_WhenValid_NotifiesPatientAndDoctor()
        {
            List<CreateNotificationRequest>? sent = null;
            await _notificationService.CreateRangeAsync(
                Arg.Do<IEnumerable<CreateNotificationRequest>>(n => sent = n.ToList()),
                Arg.Any<CancellationToken>());

            await _sut.CancelByDoctorAsync(_doctorUserId, _appointmentId, _request, _ct);

            Assert.NotNull(sent);
            Assert.Equal([_patientUserId, _doctorUserId], sent!.Select(n => n.UserId));
            Assert.All(sent, n =>
            {
                Assert.Equal(NotificationType.AppointmentCancelled, n.Type);
                Assert.Equal(_appointmentId, n.RelatedEntityId);
            });
        }

        [Fact]
        public async Task CancelByDoctorAsync_WhenWithinTwoHoursOfStart_StillCancels()
        {
            // The 2-hour window applies to patients only (API_CONTRACT §22)
            var slotStart = _now.AddMinutes(30);
            _appointment.Slot.Date = DateOnly.FromDateTime(slotStart);
            _appointment.Slot.StartTime = TimeOnly.FromDateTime(slotStart);

            var result = await _sut.CancelByDoctorAsync(_doctorUserId, _appointmentId, _request, _ct);

            Assert.True(result.IsSuccess);
            Assert.Equal(AppointmentStatus.Cancelled, _appointment.Status);
        }

        [Fact]
        public async Task CancelByDoctorAsync_WhenNotificationFails_StillReturnsSuccess()
        {
            _notificationService
                .CreateRangeAsync(Arg.Any<IEnumerable<CreateNotificationRequest>>(), Arg.Any<CancellationToken>())
                .Throws(new InvalidOperationException("notification store down"));

            var result = await _sut.CancelByDoctorAsync(_doctorUserId, _appointmentId, _request, _ct);

            Assert.True(result.IsSuccess);
            await _uow.Received(1).SaveChangesAsync(_ct);
        }

        [Fact]
        public async Task CancelByDoctorAsync_WhenDoctorProfileNotFound_ReturnsProfileNotFoundError()
        {
            var unknownUserId = Guid.NewGuid();
            _uow.Doctors.GetByUserIdAsync(unknownUserId, _ct).Returns((DoctorProfile?)null);

            var result = await _sut.CancelByDoctorAsync(unknownUserId, _appointmentId, _request, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(DoctorErrors.ProfileNotFound(unknownUserId), result.Errors);
            await _uow.Appointments.DidNotReceive().GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task CancelByDoctorAsync_WhenAppointmentNotFound_ReturnsNotFoundError()
        {
            _uow.Appointments.GetByIdAsync(999, _ct).Returns((Appointment?)null);

            var result = await _sut.CancelByDoctorAsync(_doctorUserId, 999, _request, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AppointmentErrors.NotFound(999), result.Errors);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task CancelByDoctorAsync_WhenAppointmentBelongsToAnotherDoctor_ReturnsForbiddenError()
        {
            _appointment.DoctorProfileId = _doctorId + 1;

            var result = await _sut.CancelByDoctorAsync(_doctorUserId, _appointmentId, _request, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AppointmentErrors.Forbidden, result.Errors);
            Assert.Equal(AppointmentStatus.Confirmed, _appointment.Status);
            Assert.Equal(SlotStatus.Booked, _appointment.Slot.Status);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
            await _notificationService.DidNotReceive()
                .CreateRangeAsync(Arg.Any<IEnumerable<CreateNotificationRequest>>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task CancelByDoctorAsync_WhenAlreadyCancelled_ReturnsAlreadyCancelledError()
        {
            _appointment.Status = AppointmentStatus.Cancelled;

            var result = await _sut.CancelByDoctorAsync(_doctorUserId, _appointmentId, _request, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AppointmentErrors.AlreadyCancelled, result.Errors);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task CancelByDoctorAsync_WhenCompleted_ReturnsAlreadyCompletedError()
        {
            _appointment.Status = AppointmentStatus.Completed;

            var result = await _sut.CancelByDoctorAsync(_doctorUserId, _appointmentId, _request, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AppointmentErrors.AlreadyCompleted, result.Errors);
            Assert.Equal(SlotStatus.Booked, _appointment.Slot.Status);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }
}
