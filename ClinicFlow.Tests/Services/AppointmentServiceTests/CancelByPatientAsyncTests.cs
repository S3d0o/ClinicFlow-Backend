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
    public class CancelByPatientAsyncTests : AppointmentServiceTestsBase
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
        public async Task CancelByPatientAsync_WhenPatientProfileIdsNotMatch_ReturnsUnauthorizedError()
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

            var error = AppointmentErrors.Unauthorized;

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
                    Status = AppointmentStatus.Cancelled
                });

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
                    Status = AppointmentStatus.Completed
                });

            var error = AppointmentErrors.AlreadyCompleted;

            //Act
            var result = await _sut.CancelByPatientAsync(_patientUserId, _appointmentId, _request, _ct);

            //Assert
            Assert.True(result.IsFailure);
            Assert.Contains(error, result.Errors);
            await _uow.DidNotReceive().SaveChangesAsync();
        }

        [Fact]
        public async Task CancelByPatientAsync_WhenCancellsWithIn2Hours_ReturnsCancellationWindowExpiredError()
        {
            //Arrange
            _uow.Patients.GetPatientByUserIdAsync(_patientUserId, _ct)
                .Returns(new PatientProfile
                {
                    Id = _patientId,
                });
            _dateTimeProvider.UtcNow.Returns(_now);
            var slotDateTime = _now.AddHours(1);
            var slot = new AppointmentSlot
            {
                Id = 1,
                Date = DateOnly.FromDateTime(slotDateTime),
                StartTime = TimeOnly.FromDateTime(slotDateTime)
            };
            _uow.Appointments.GetByIdAsync(_appointmentId, _ct)
                .Returns(new Appointment
                {
                    SlotId = slot.Id,
                    Slot = slot,
                    PatientProfileId = _patientId,
                    Status = AppointmentStatus.Confirmed,
                });

            var error = AppointmentErrors.CancellationWindowExpired;

            //Act
            var result = await _sut.CancelByPatientAsync(_patientUserId, _appointmentId, _request, _ct);

            //Assert
            Assert.True(result.IsFailure);
            Assert.Contains(error, result.Errors);
            await _uow.DidNotReceive().SaveChangesAsync();
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

        private async Task SetupHappyPath()
        {
            _uow.Patients.GetPatientByUserIdAsync(_patientUserId, _ct)
                  .Returns(new PatientProfile
                  {
                      Id = _patientId,
                  });
            _dateTimeProvider.UtcNow.Returns(_now);
            var slotDateTime = _now.AddHours(10);
            var slot = new AppointmentSlot
            {
                Id = 1,
                Date = DateOnly.FromDateTime(slotDateTime),
                StartTime = TimeOnly.FromDateTime(slotDateTime)
            };
            _uow.Appointments.GetByIdAsync(_appointmentId, _ct)
                .Returns(new Appointment
                {
                    SlotId = slot.Id,
                    Slot = slot,
                    PatientProfileId = _patientId,
                    Status = AppointmentStatus.Confirmed,
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
                });

        }
    }
}
