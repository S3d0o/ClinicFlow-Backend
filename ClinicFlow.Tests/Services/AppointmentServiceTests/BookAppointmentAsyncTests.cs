using Domain.Entities.AppModule;
using Domain.Entities.IdentityModule;
using Domain.Interfaces;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shared.DTOs.Appointment;
using Shared.DTOs.Notification;
using Shared.Errors;
using Shared.ResultPattern;
using ClinicFlow.Domain.Enums;

namespace ClinicFlow.Tests.Services.AppointmentServiceTests
{
    public class BookAppointmentAsyncTests : AppointmentServiceTestsBase
    {
        // Shared constants
        private readonly Guid _patientUserId = Guid.NewGuid();
        private readonly CancellationToken _ct = CancellationToken.None;
        private const int SlotId = 1;
        private readonly DateTime _now = new DateTime(2026, 6, 15, 10, 0, 0);
        private readonly BookAppointmentRequest _request = new(SlotId, "no reason");

        // Happy path helper — call this then add your one difference on top
        private (ITransaction transaction, AppointmentResponse response) SetupHappyPath()
        {
            var dateTime = _now.AddDays(1).AddHours(1);

            _uow.Patients
                .GetPatientByUserIdAsync(_patientUserId, _ct)
                .Returns(new PatientProfile
                {
                    UserId = _patientUserId,
                    User = new ApplicationUser { FirstName = "Mona", LastName = "Hassan", Email = "mona@test.com" }
                });

            _dateTimeProvider.ClinicNow.Returns(_now);

            _uow.Slots.GetByIdAsync(SlotId, _ct).Returns(new AppointmentSlot
            {
                Id = SlotId,
                DoctorProfile = ApprovedDoctor(),
                Status = SlotStatus.Available,
                Date = DateOnly.FromDateTime(dateTime),
                StartTime = TimeOnly.FromDateTime(dateTime),
            });

            var transaction = Substitute.For<ITransaction>();
            _uow.RunsTransactionsWith<Result<Appointment>>(transaction);
            _uow.Slots.SetStatusFromAvailableToBookedAsync(SlotId, _ct).Returns(1);

            var appointmentResponse = new AppointmentResponse
            {
                SlotId = SlotId,
                AppointmentDate = DateOnly.FromDateTime(dateTime),
                StartTime = TimeOnly.FromDateTime(dateTime)
            };
            _mapper.Map<AppointmentResponse>(Arg.Any<Appointment>())
                   .Returns(appointmentResponse);

            return (transaction, appointmentResponse);
        }

        [Fact]
        public async Task BookAppointmentAsync_WhenPatientProfileNotFound_ReturnsProfileNotFoundError()
        {
            // Arrange
            _uow.Patients
                .GetPatientByUserIdAsync(_patientUserId, _ct)
                .Returns((PatientProfile?)null);

            var error = PatientErrors.ProfileNotFound(_patientUserId);

            // Act
            var result = await _sut.BookAppointmentAsync(_patientUserId, _request, _ct);

            // Assert
            Assert.True(result.IsFailure);
            Assert.Contains(error, result.Errors);
            await _uow.Slots.DidNotReceive()
                      .GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task BookAppointmentAsync_WhenSlotNotFound_ReturnsNotFoundError()
        {
            // Arrange
            _uow.Patients
                .GetPatientByUserIdAsync(_patientUserId, _ct)
                .Returns(new PatientProfile());

            _uow.Slots.GetByIdAsync(SlotId, _ct)
                .Returns((AppointmentSlot?)null);

            var error = AppointmentErrors.SlotNotFound(SlotId);

            // Act
            var result = await _sut.BookAppointmentAsync(_patientUserId, _request, _ct);

            // Assert
            Assert.True(result.IsFailure);
            Assert.Contains(error, result.Errors);
            await _uow.Slots.DidNotReceive()
                      .SetStatusFromAvailableToBookedAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(SlotStatus.Booked)]
        [InlineData(SlotStatus.Blocked)]
        public async Task BookAppointmentAsync_WhenSlotIsNotAvailable_ReturnsSlotNotAvailableError(SlotStatus status)
        {
            // Arrange
            _uow.Patients
                .GetPatientByUserIdAsync(_patientUserId, _ct)
                .Returns(new PatientProfile());

            _uow.Slots.GetByIdAsync(SlotId, _ct).Returns(new AppointmentSlot
            {
                DoctorProfile = ApprovedDoctor(),
                Status = status
            });

            // Act
            var result = await _sut.BookAppointmentAsync(_patientUserId, _request, _ct);

            // Assert
            Assert.True(result.IsFailure);
            Assert.Contains(AppointmentErrors.SlotNotAvailable, result.Errors);
            await _uow.Slots.DidNotReceive()
                      .SetStatusFromAvailableToBookedAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(-1, 0)]   // yesterday, same time
        [InlineData(0, -1)]   // today, 1 hour ago
        public async Task BookAppointmentAsync_WhenSlotIsInPast_ReturnsSlotInPastError(
            int daysOffset, int hoursOffset)
        {
            // Arrange
            _uow.Patients
                .GetPatientByUserIdAsync(_patientUserId, _ct)
                .Returns(new PatientProfile());

            _dateTimeProvider.ClinicNow.Returns(_now);

            var slotDateTime = _now.AddDays(daysOffset).AddHours(hoursOffset);
            _uow.Slots.GetByIdAsync(SlotId, _ct).Returns(new AppointmentSlot
            {
                DoctorProfile = ApprovedDoctor(),
                Status = SlotStatus.Available,
                Date = DateOnly.FromDateTime(slotDateTime),
                StartTime = TimeOnly.FromDateTime(slotDateTime),
            });

            // Act
            var result = await _sut.BookAppointmentAsync(_patientUserId, _request, _ct);

            // Assert
            Assert.True(result.IsFailure);
            Assert.Contains(AppointmentErrors.SlotInPast, result.Errors);
            await _uow.Slots.DidNotReceive()
                      .SetStatusFromAvailableToBookedAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task BookAppointmentAsync_WhenSlotStartedOnClinicClockButNotInUtc_ReturnsSlotInPastError()
        {
            // REVIEW.md M2: 10:00 clinic time (UTC+3) is 07:00 UTC. A 09:00 slot has started at the clinic,
            // although 09:00 is still ahead of the UTC clock
            _uow.Patients.GetPatientByUserIdAsync(_patientUserId, _ct).Returns(new PatientProfile());
            _dateTimeProvider.ClinicNow.Returns(_now);
            _dateTimeProvider.UtcNow.Returns(_now.AddHours(-3));
            _uow.Slots.GetByIdAsync(SlotId, _ct).Returns(new AppointmentSlot
            {
                DoctorProfile = ApprovedDoctor(),
                Status = SlotStatus.Available,
                Date = DateOnly.FromDateTime(_now),
                StartTime = new TimeOnly(9, 0),
            });

            var result = await _sut.BookAppointmentAsync(_patientUserId, _request, _ct);

            Assert.Contains(AppointmentErrors.SlotInPast, result.Errors);
        }

        [Fact]
        public async Task BookAppointmentAsync_WhenConcurrencyConflict_ReturnsSlotAlreadyBookedError()
        {
            // Arrange
            _uow.Patients
                .GetPatientByUserIdAsync(_patientUserId, _ct)
                .Returns(new PatientProfile());

            _dateTimeProvider.ClinicNow.Returns(_now);

            var dateTime = _now.AddDays(1).AddHours(1);
            _uow.Slots.GetByIdAsync(SlotId, _ct).Returns(new AppointmentSlot
            {
                DoctorProfile = ApprovedDoctor(),
                Status = SlotStatus.Available,
                Date = DateOnly.FromDateTime(dateTime),
                StartTime = TimeOnly.FromDateTime(dateTime),
            });

            var transaction = Substitute.For<ITransaction>();
            _uow.RunsTransactionsWith<Result<Appointment>>(transaction);
            _uow.Slots.SetStatusFromAvailableToBookedAsync(SlotId, _ct).Returns(0);

            // Act
            var result = await _sut.BookAppointmentAsync(_patientUserId, _request, _ct);

            // Assert
            Assert.True(result.IsFailure);
            Assert.Contains(AppointmentErrors.SlotAlreadyBooked, result.Errors);
            await transaction.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
            await _uow.Appointments.DidNotReceive()
                      .AddAsync(Arg.Any<Appointment>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task BookAppointmentAsync_WhenAllInputsAreValid_ReturnsSuccess()
        {
            // Arrange
            var (transaction, appointmentResponse) = SetupHappyPath();

            // Act
            var result = await _sut.BookAppointmentAsync(_patientUserId, _request, _ct);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal(appointmentResponse, result.Value);
            await _uow.Received(1).ExecuteInTransactionAsync(Arg.Any<Func<ITransaction, Task<Result<Appointment>>>>(), _ct);
            await _uow.Slots.Received(1)
                      .SetStatusFromAvailableToBookedAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
            await _uow.Appointments.Received(1)
                      .AddAsync(Arg.Any<Appointment>(), Arg.Any<CancellationToken>());
            await _uow.Received(1).SaveChangesAsync();
            await transaction.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task BookAppointmentAsync_WhenBooked_NotifiesAndEmailsOnlyAfterTheCommit()
        {
            // A retried transaction re-runs its operation, so side effects must stay outside it (REVIEW.md NL14)
            var (transaction, _) = SetupHappyPath();

            await _sut.BookAppointmentAsync(_patientUserId, _request, _ct);

            Received.InOrder(() =>
            {
                transaction.CommitAsync(Arg.Any<CancellationToken>());
                _notificationService.CreateRangeAsync(Arg.Any<IEnumerable<CreateNotificationRequest>>(), Arg.Any<CancellationToken>());
                _emailService.SendAppointmentConfirmationAsync(
                    Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<TimeOnly>(), Arg.Any<CancellationToken>());
            });
        }

        [Fact]
        public async Task BookAppointmentAsync_WhenSlotAlreadyBooked_SendsNoNotificationOrEmail()
        {
            SetupHappyPath();
            _uow.Slots.SetStatusFromAvailableToBookedAsync(SlotId, _ct).Returns(0);

            await _sut.BookAppointmentAsync(_patientUserId, _request, _ct);

            await _notificationService.DidNotReceiveWithAnyArgs().CreateRangeAsync(default!, default);
            await _emailService.DidNotReceiveWithAnyArgs().SendAppointmentConfirmationAsync(default!, default!, default!, default, default, default);
        }

        [Fact]
        public async Task BookAppointmentAsync_WhenTheTransactionFails_ReturnsFailureAndSendsNothing()
        {
            // E.g. a transient SQL error that outlasted every retry
            SetupHappyPath();
            _uow.ExecuteInTransactionAsync(Arg.Any<Func<ITransaction, Task<Result<Appointment>>>>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new TimeoutException("simulated"));

            var result = await _sut.BookAppointmentAsync(_patientUserId, _request, _ct);

            Assert.True(result.IsFailure);
            Assert.Equal(ErrorType.Failure, result.Errors[0].Type);
            await _notificationService.DidNotReceiveWithAnyArgs().CreateRangeAsync(default!, default);
            await _emailService.DidNotReceiveWithAnyArgs().SendAppointmentConfirmationAsync(default!, default!, default!, default, default, default);
        }

        [Fact]
        public async Task BookAppointmentAsync_WhenNotificationFails_StillReturnsSuccess()
        {
            // Arrange
            var (transaction, _) = SetupHappyPath();

            _notificationService
                .CreateRangeAsync(
                    Arg.Any<IEnumerable<CreateNotificationRequest>>(),
                    Arg.Any<CancellationToken>())
                .Throws<Exception>();

            // Act
            var result = await _sut.BookAppointmentAsync(_patientUserId, _request, _ct);

            // Assert
            Assert.True(result.IsSuccess);
            await transaction.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(false, true)]  // not approved yet
        [InlineData(true, false)]  // suspended
        public async Task BookAppointmentAsync_WhenDoctorNotApprovedOrSuspended_ReturnsSlotNotFoundAndBooksNothing(bool approved, bool active)
        {
            // REVIEW.md M1: these slots aren't listed, so booking them directly must fail the same way
            SetupHappyPath();
            _uow.Slots.GetByIdAsync(SlotId, _ct).Returns(new AppointmentSlot
            {
                Id = SlotId,
                DoctorProfile = new DoctorProfile { IsApprovedByAdmin = approved, User = new ApplicationUser { IsActive = active } },
                Status = SlotStatus.Available,
                Date = DateOnly.FromDateTime(_now.AddDays(2)),
                StartTime = new TimeOnly(10, 0)
            });

            var result = await _sut.BookAppointmentAsync(_patientUserId, new BookAppointmentRequest(SlotId, null), _ct);

            Assert.True(result.IsFailure);
            Assert.Equal(AppointmentErrors.SlotNotFound(SlotId), Assert.Single(result.Errors));
            await _uow.Slots.DidNotReceive().SetStatusFromAvailableToBookedAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        }

        private static DoctorProfile ApprovedDoctor() => new()
        {
            IsApprovedByAdmin = true,
            User = new ApplicationUser { FirstName = "Ahmed", LastName = "Elsayed", IsActive = true }
        };
    }
}
