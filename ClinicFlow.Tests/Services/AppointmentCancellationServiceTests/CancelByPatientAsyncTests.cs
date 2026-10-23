using Domain.Entities.AppModule;
using Domain.Entities.IdentityModule;
using Domain.Enums;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shared.DTOs.Appointment;
using Shared.DTOs.Notification;
using Shared.Errors;
using Domain.Errors;

namespace ClinicFlow.Tests.Services.AppointmentCancellationServiceTests
{
    public class CancelByPatientAsyncTests : AppointmentCancellationServiceTestsBase
    {
        // Shared constants
        private readonly Guid _patientUserId = Guid.NewGuid();
        private readonly CancellationToken _ct = CancellationToken.None;
        private const int _appointmentId = 1;
        private readonly CancelAppointmentRequest _request = new CancelAppointmentRequest("reason");
        private const int _patientId = 2;
        private readonly DateTime _now = new DateTime(2026, 6, 15, 10, 0, 0);



        [Fact]
        public async Task CancelByPatientAsync_WhenPatientProfileNotFound_ReturnsProfileNotFoundError()
        {
            //Arrange
            _uow.Patients.GetPatientByUserIdAsync(_patientUserId, _ct)
                .Returns((PatientProfile?)null);

            var error = PatientErrors.ProfileNotFound(_patientUserId);

            //Act
            var result = await _sut.CancelByPatientAsync(_patientUserId, _appointmentId, _request, _ct);

            //Assert
            Assert.True(result.IsFailure);
            Assert.Contains(error, result.Errors);
            await _uow.Appointments.DidNotReceive().GetByIdAsync(_appointmentId, _ct);

        }

        [Fact]
        public async Task CancelByPatientAsync_WhenAppointmentNotFound_ReturnsNotFoundError()
        {
            //Arrange
            _uow.Patients.GetPatientByUserIdAsync(_patientUserId, _ct)
                .Returns(new PatientProfile());
            _uow.Appointments.GetByIdAsync(_appointmentId, _ct)
                .Returns((Appointment?)null);

            var error = AppointmentErrors.NotFound(_appointmentId);

            //Act
            var result = await _sut.CancelByPatientAsync(_patientUserId, _appointmentId, _request, _ct);

            //Assert
            Assert.True(result.IsFailure);
            Assert.Contains(error, result.Errors);
            await _uow.DidNotReceive().SaveChangesAsync();

        }

        [Fact]
        public async Task CancelByPatientAsync_WhenPatientProfileIdsNotMatch_ReturnsForbiddenError()
        {
            //Arrange
            _uow.Patients.GetPatientByUserIdAsync(_patientUserId, _ct)
                .Returns(new PatientProfile
                {
                    Id = _patientId,
                });
            _uow.Appointments.GetByIdAsync(_appointmentId, _ct)
                .Returns(new Appointment
                {
                    PatientProfileId = _patientId + 1
                });

            var error = AppointmentErrors.Forbidden;

            //Act
            var result = await _sut.CancelByPatientAsync(_patientUserId, _appointmentId, _request, _ct);

            //Assert
            Assert.True(result.IsFailure);
            Assert.Contains(error, result.Errors);
            await _uow.DidNotReceive().SaveChangesAsync();
        }

        [Fact]
        public async Task CancelByPatientAsync_WhenAppointmentStatusIsCancelled_ReturnsAlreadyCancelledError()
        {
            //Arrange
            _uow.Patients.GetPatientByUserIdAsync(_patientUserId, _ct)
                .Returns(new PatientProfile
                {
                    Id = _patientId,
                });
            _uow.Appointments.GetByIdAsync(_appointmentId, _ct)
                .Returns(new Appointment
                {
                    PatientProfileId = _patientId,
                }.WithStatus(AppointmentStatus.Cancelled));

            var error = AppointmentErrors.AlreadyCancelled;

            //Act
            var result = await _sut.CancelByPatientAsync(_patientUserId, _appointmentId, _request, _ct);

            //Assert
            Assert.True(result.IsFailure);
            Assert.Contains(error, result.Errors);
            await _uow.DidNotReceive().SaveChangesAsync();
        }

        [Fact]
        public async Task CancelByPatientAsync_WhenAppointmentStatusIsCompleted_ReturnsAlreadyCompletedError()
        {
            //Arrange
            _uow.Patients.GetPatientByUserIdAsync(_patientUserId, _ct)
                .Returns(new PatientProfile
                {
                    Id = _patientId,
                });
            _uow.Appointments.GetByIdAsync(_appointmentId, _ct)
                .Returns(new Appointment
                {
                    PatientProfileId = _patientId,
                }.WithStatus(AppointmentStatus.Completed));

            var error = AppointmentErrors.AlreadyCompleted;

            //Act
            var result = await _sut.CancelByPatientAsync(_patientUserId, _appointmentId, _request, _ct);

            //Assert
            Assert.True(result.IsFailure);
            Assert.Contains(error, result.Errors);
            await _uow.DidNotReceive().SaveChangesAsync();
        }

        [Theory]
        [InlineData(119)] // a minute inside the 2-hour window
        [InlineData(60)]
        [InlineData(-30)] // already started
        public async Task CancelByPatientAsync_WhenCancellsWithIn2Hours_ReturnsCancellationWindowExpiredError(int minutesUntilStart)
        {
            //Arrange
            await SetupHappyPath(minutesUntilStart);

            //Act
            var result = await _sut.CancelByPatientAsync(_patientUserId, _appointmentId, _request, _ct);

            //Assert
            Assert.True(result.IsFailure);
            Assert.Contains(AppointmentErrors.CancellationWindowExpired, result.Errors);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task CancelByPatientAsync_ExactlyTwoHoursBeforeStart_Cancels()
        {
            await SetupHappyPath(minutesUntilStart: 120);

            var result = await _sut.CancelByPatientAsync(_patientUserId, _appointmentId, _request, _ct);

            Assert.True(result.IsSuccess);
        }

        [Fact]
        public async Task CancelByPatientAsync_MeasuresTheWindowOnTheSlotsUtcStart()
        {
            // REVIEW.md M2: a 12:00 slot in Cairo (UTC+3) starts at 09:00 UTC. At 07:30 UTC it is 1.5 hours away,
            // although "12:00" is 4.5 hours after "07:30"
            await SetupHappyPath();
            _clock.UtcNow.Returns(new DateTime(2026, 10, 19, 7, 30, 0, DateTimeKind.Utc));
            var slot = (await _uow.Appointments.GetByIdAsync(_appointmentId, _ct))!.Slot;
            (slot.Date, slot.StartTime, slot.StartsAtUtc) =
                (new DateOnly(2026, 10, 19), new TimeOnly(12, 0), new DateTime(2026, 10, 19, 9, 0, 0, DateTimeKind.Utc));

            var result = await _sut.CancelByPatientAsync(_patientUserId, _appointmentId, _request, _ct);

            Assert.Contains(AppointmentErrors.CancellationWindowExpired, result.Errors);
        }

        [Fact]
        public async Task CancelByPatientAsync_WhenWhenAllInputsAreValid_ReturnsSuccess()
        {
            //Arrange
            await SetupHappyPath();
            //Act
            var result = await _sut.CancelByPatientAsync(_patientUserId, _appointmentId, _request, _ct);

            //Assert
            Assert.True(result.IsSuccess);
            await _uow.Received(1).SaveChangesAsync(_ct);
        }

        [Fact]
        public async Task CancelByPatientAsync_WhenNotificationFails_StillReturnsSuccess()
        {
            //Arrange
            await SetupHappyPath();
            _notificationService
                            .CreateRangeAsync(
                                Arg.Any<IEnumerable<CreateNotificationRequest>>(),
                                Arg.Any<CancellationToken>())
                            .Throws<Exception>();
            
            //Act
            var result = await _sut.CancelByPatientAsync(_patientUserId, _appointmentId, _request, _ct);

            //Assert
            Assert.True(result.IsSuccess);
            await _uow.Received(1).SaveChangesAsync(_ct);
        }

        private async Task SetupHappyPath(int minutesUntilStart = 600)
        {
            _uow.Patients.GetPatientByUserIdAsync(_patientUserId, _ct)
                  .Returns(new PatientProfile
                  {
                      Id = _patientId,
                  });
            var slotDateTime = _now.AddMinutes(minutesUntilStart);
            var slot = new AppointmentSlot
            {
                Id = 1,
                Date = DateOnly.FromDateTime(slotDateTime),
                StartTime = TimeOnly.FromDateTime(slotDateTime),
                StartsAtUtc = UtcNow.AddMinutes(minutesUntilStart),
                DoctorSchedule = new DoctorSchedule { StartTime = TimeOnly.MinValue, EndTime = TimeOnly.MaxValue }
            };
            _uow.Appointments.GetByIdAsync(_appointmentId, _ct)
                .Returns(new Appointment
                {
                    SlotId = slot.Id,
                    Slot = slot,
                    PatientProfileId = _patientId,
                    Doctor = new DoctorProfile
                    {
                        UserId = Guid.NewGuid()
                    },

                    Patient = new PatientProfile
                    {
                        User = new ApplicationUser
                        {
                            FirstName = "Saad",
                            LastName = "Mohamed"
                        }
                    }
                }.WithStatus(AppointmentStatus.Confirmed));

        }
    }
}
