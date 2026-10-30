using Domain.Entities.AppModule;
using Domain.Entities.IdentityModule;
using Domain.Interfaces;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shared.DTOs.Appointment;
using Domain.Events;
using Shared.Errors;
using Domain.Errors;
using Domain.ResultPattern;
using ClinicFlow.Domain.Enums;

namespace ClinicFlow.Tests.Services.AppointmentBookingServiceTests
{
    public class BookAppointmentAsyncTests : AppointmentBookingServiceTestsBase
    {
        // Shared constants
        private readonly Guid _patientUserId = Guid.NewGuid();
        private readonly CancellationToken _ct = CancellationToken.None;
        private const int SlotId = 1;
        private readonly DateTime _now = new DateTime(2026, 6, 15, 10, 0, 0);
        private readonly BookAppointmentRequest _request = new(SlotId, "no reason");
        private const int SlotBranchId = 3;

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

            _uow.Slots.GetByIdAsync(SlotId, _ct).Returns(AvailableSlot(UtcNow.AddDays(1), dateTime));

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

        [Fact]
        public async Task BookAppointmentAsync_WhenSlotIsAtADeactivatedBranch_ReturnsSlotNotAvailableError()
        {
            // Arrange
            _uow.Patients
                .GetPatientByUserIdAsync(_patientUserId, _ct)
                .Returns(new PatientProfile());

            _uow.Slots.GetByIdAsync(SlotId, _ct).Returns(new AppointmentSlot
            {
                DoctorProfile = ApprovedDoctor(),
                BranchId = 2,
                Status = SlotStatus.Available
            });
            _uow.Branches.IsActiveAsync(2, _ct).Returns(false);

            // Act
            var result = await _sut.BookAppointmentAsync(_patientUserId, _request, _ct);

            // Assert
            Assert.Contains(AppointmentErrors.SlotNotAvailable, result.Errors);
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
        [InlineData(-24 * 60)] // yesterday
        [InlineData(-1)]       // started a minute ago
        public async Task BookAppointmentAsync_WhenSlotHasStarted_ReturnsSlotInPastError(int minutesFromNow)
        {
            // Arrange
            _uow.Patients
                .GetPatientByUserIdAsync(_patientUserId, _ct)
                .Returns(new PatientProfile());
            _uow.Slots.GetByIdAsync(SlotId, _ct).Returns(AvailableSlot(UtcNow.AddMinutes(minutesFromNow), _now));

            // Act
            var result = await _sut.BookAppointmentAsync(_patientUserId, _request, _ct);

            // Assert
            Assert.True(result.IsFailure);
            Assert.Contains(AppointmentErrors.SlotInPast, result.Errors);
            await _uow.Slots.DidNotReceive()
                      .SetStatusFromAvailableToBookedAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task BookAppointmentAsync_WhenSlotStartsNow_Books()
        {
            SetupHappyPath();
            _uow.Slots.GetByIdAsync(SlotId, _ct).Returns(AvailableSlot(UtcNow, _now));

            var result = await _sut.BookAppointmentAsync(_patientUserId, _request, _ct);

            Assert.True(result.IsSuccess);
        }

        [Fact]
        public async Task BookAppointmentAsync_ComparesTheSlotsUtcStartNotItsWallClockTime()
        {
            // REVIEW.md M2: a 09:00 slot in Cairo (UTC+3) starts at 06:00 UTC. At 07:00 UTC it has started,
            // although "09:00" is later than "07:00"
            _uow.Patients.GetPatientByUserIdAsync(_patientUserId, _ct).Returns(new PatientProfile());
            _clock.UtcNow.Returns(new DateTime(2026, 10, 19, 7, 0, 0, DateTimeKind.Utc));
            _uow.Slots.GetByIdAsync(SlotId, _ct).Returns(
                AvailableSlot(new DateTime(2026, 10, 19, 6, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 19, 9, 0, 0)));

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

            _uow.Slots.GetByIdAsync(SlotId, _ct).Returns(AvailableSlot(UtcNow.AddDays(1), _now.AddDays(1)));

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
                      .AddAsync(Arg.Is<Appointment>(a => a!.BookedAt == UtcNow && a.BranchId == SlotBranchId), Arg.Any<CancellationToken>()); // time from IClock, branch from the slot
            await _uow.Received(2).SaveChangesAsync(_ct); // the appointment, then its AppointmentBooked event
            await transaction.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task BookAppointmentAsync_WhenBooked_SavesTheEventBeforeTheCommit()
        {
            // The event needs the inserted id, and is saved in the same transaction: the notifications and the
            // email go out if, and only if, the booking commits (docs/v2/06 §5)
            var (transaction, _) = SetupHappyPath();

            await _sut.BookAppointmentAsync(_patientUserId, _request, _ct);

            Received.InOrder(() =>
            {
                _uow.Appointments.AddAsync(Arg.Any<Appointment>(), Arg.Any<CancellationToken>());
                _uow.SaveChangesAsync(_ct);
                _uow.Outbox.Add(Arg.Any<AppointmentBooked>());
                _uow.SaveChangesAsync(_ct);
                transaction.CommitAsync(Arg.Any<CancellationToken>());
            });
        }

        [Fact]
        public async Task BookAppointmentAsync_WhenSlotAlreadyBooked_QueuesNothing()
        {
            SetupHappyPath();
            _uow.Slots.SetStatusFromAvailableToBookedAsync(SlotId, _ct).Returns(0);

            await _sut.BookAppointmentAsync(_patientUserId, _request, _ct);

            _uow.Outbox.DidNotReceiveWithAnyArgs().Add(default!);
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
            _uow.Outbox.DidNotReceiveWithAnyArgs().Add(default!);
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
                StartTime = new TimeOnly(10, 0),
                StartsAtUtc = UtcNow.AddDays(2)
            });

            var result = await _sut.BookAppointmentAsync(_patientUserId, new BookAppointmentRequest(SlotId, null), _ct);

            Assert.True(result.IsFailure);
            Assert.Equal(AppointmentErrors.SlotNotFound(SlotId), Assert.Single(result.Errors));
            await _uow.Slots.DidNotReceive().SetStatusFromAvailableToBookedAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        }

        // An available slot of an approved doctor; startsAtUtc drives the rules, wallClock is what the patient sees
        private static AppointmentSlot AvailableSlot(DateTime startsAtUtc, DateTime wallClock) => new()
        {
            Id = SlotId,
            DoctorProfile = ApprovedDoctor(),
            Status = SlotStatus.Available,
            BranchId = SlotBranchId,
            Date = DateOnly.FromDateTime(wallClock),
            StartTime = TimeOnly.FromDateTime(wallClock),
            StartsAtUtc = startsAtUtc,
            EndsAtUtc = startsAtUtc.AddMinutes(30),
        };

        private static DoctorProfile ApprovedDoctor() => new()
        {
            IsApprovedByAdmin = true,
            User = new ApplicationUser { FirstName = "Ahmed", LastName = "Elsayed", IsActive = true }
        };
    }
}
