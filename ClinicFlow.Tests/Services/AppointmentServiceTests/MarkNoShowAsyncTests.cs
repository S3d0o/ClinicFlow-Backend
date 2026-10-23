using Domain.Entities.AppModule;
using Domain.Entities.IdentityModule;
using Domain.Enums;
using NSubstitute;
using Shared.DTOs.Notification;
using Shared.Errors;

namespace ClinicFlow.Tests.Services.AppointmentServiceTests
{
    // REVIEW.md M14: NoShow was never set; the owning doctor or an admin now marks it after a 15-minute grace period
    public class MarkNoShowAsyncTests : AppointmentServiceTestsBase
    {
        private const int _appointmentId = 1;
        private readonly Guid _doctorUserId = Guid.NewGuid();
        private readonly Guid _patientUserId = Guid.NewGuid();
        private readonly CancellationToken _ct = CancellationToken.None;
        private readonly Appointment _appointment;

        public MarkNoShowAsyncTests()
        {
            // The visit was due at 10:00 in Cairo (07:00 UTC) and "now" is 07:15 UTC, the end of the grace period
            _clock.UtcNow.Returns(new DateTime(2026, 10, 18, 7, 15, 0, DateTimeKind.Utc));
            _appointment = new Appointment
            {
                Id = _appointmentId,
                Status = AppointmentStatus.Confirmed,
                Doctor = new DoctorProfile { UserId = _doctorUserId },
                Patient = new PatientProfile { UserId = _patientUserId, User = new ApplicationUser() },
                Slot = new AppointmentSlot
                {
                    Date = new DateOnly(2026, 10, 18), StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(10, 30),
                    StartsAtUtc = new DateTime(2026, 10, 18, 7, 0, 0, DateTimeKind.Utc)
                }
            };
            _uow.Appointments.GetByIdAsync(_appointmentId, _ct).Returns(_appointment);
        }

        [Fact]
        public async Task MarkNoShowAsync_WhenOwningDoctorAfterGracePeriod_MarksNoShowSavesAndNotifiesPatient()
        {
            List<CreateNotificationRequest>? sent = null;
            await _notificationService.CreateRangeAsync(
                Arg.Do<IEnumerable<CreateNotificationRequest>>(n => sent = n.ToList()),
                Arg.Any<CancellationToken>());

            var result = await _sut.MarkNoShowAsync(_doctorUserId, "Doctor", _appointmentId, _ct);

            Assert.True(result.IsSuccess);
            Assert.Equal(AppointmentStatus.NoShow, _appointment.Status);
            await _uow.Received(1).SaveChangesAsync(_ct);
            var notification = Assert.Single(sent!);
            Assert.Equal(_patientUserId, notification.UserId);
            Assert.Equal(_appointmentId, notification.RelatedEntityId);
        }

        [Fact]
        public async Task MarkNoShowAsync_WhenAdmin_Succeeds()
        {
            var result = await _sut.MarkNoShowAsync(Guid.NewGuid(), "Admin", _appointmentId, _ct);

            Assert.True(result.IsSuccess);
            Assert.Equal(AppointmentStatus.NoShow, _appointment.Status);
        }

        [Fact]
        public async Task MarkNoShowAsync_WhenAnotherDoctor_ReturnsUnauthorizedAndChangesNothing()
        {
            var result = await _sut.MarkNoShowAsync(Guid.NewGuid(), "Doctor", _appointmentId, _ct);

            Assert.Contains(AppointmentErrors.Forbidden, result.Errors);
            Assert.Equal(AppointmentStatus.Confirmed, _appointment.Status);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task MarkNoShowAsync_WhenAppointmentNotFound_ReturnsNotFound()
        {
            var result = await _sut.MarkNoShowAsync(_doctorUserId, "Doctor", 99, _ct);

            Assert.Contains(AppointmentErrors.NotFound(99), result.Errors);
        }

        [Fact]
        public async Task MarkNoShowAsync_WithinGracePeriod_ReturnsNoShowTooEarlyAndChangesNothing()
        {
            _clock.UtcNow.Returns(new DateTime(2026, 10, 18, 7, 14, 0, DateTimeKind.Utc));

            var result = await _sut.MarkNoShowAsync(_doctorUserId, "Doctor", _appointmentId, _ct);

            Assert.Contains(AppointmentErrors.NoShowTooEarly, result.Errors);
            Assert.Equal(AppointmentStatus.Confirmed, _appointment.Status);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        public static TheoryData<AppointmentStatus, string> ClosedStatuses => new()
        {
            { AppointmentStatus.Cancelled, "Appointment.AlreadyCancelled" },
            { AppointmentStatus.Completed, "Appointment.AlreadyCompleted" },
            { AppointmentStatus.NoShow, "Appointment.AlreadyNoShow" }
        };

        [Theory]
        [MemberData(nameof(ClosedStatuses))]
        public async Task MarkNoShowAsync_WhenAppointmentIsClosed_ReturnsConflict(AppointmentStatus status, string expectedCode)
        {
            _appointment.Status = status;

            var result = await _sut.MarkNoShowAsync(_doctorUserId, "Doctor", _appointmentId, _ct);

            Assert.Equal(expectedCode, Assert.Single(result.Errors).Code);
            Assert.Equal(status, _appointment.Status);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }
}
