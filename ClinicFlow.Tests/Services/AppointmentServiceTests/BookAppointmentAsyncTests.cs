using Domain.Entities.AppModule;
using Microsoft.EntityFrameworkCore.Storage;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shared.DTOs.Appointment;
using Shared.DTOs.Notification;
using Shared.Errors;
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
        private (IDbContextTransaction transaction, AppointmentResponse response) SetupHappyPath()
        {
            var dateTime = _now.AddDays(1).AddHours(1);

            _uow.Patients
                .GetPatientByUserIdAsync(_patientUserId, _ct)
                .Returns(new PatientProfile());

            _dateTimeProvider.UtcNow.Returns(_now);

            _uow.Slots.GetByIdAsync(SlotId, _ct).Returns(new AppointmentSlot
            {
                Id = SlotId,
                Status = SlotStatus.Available,
                Date = DateOnly.FromDateTime(dateTime),
                StartTime = TimeOnly.FromDateTime(dateTime),
            });

            var transaction = Substitute.For<IDbContextTransaction>();
            _uow.BeginTransactionAsync().Returns(transaction);
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

            var error = AppointmentErrors.NotFound(SlotId);

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

            _dateTimeProvider.UtcNow.Returns(_now);

            var slotDateTime = _now.AddDays(daysOffset).AddHours(hoursOffset);
            _uow.Slots.GetByIdAsync(SlotId, _ct).Returns(new AppointmentSlot
            {
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
        public async Task BookAppointmentAsync_WhenConcurrencyConflict_ReturnsSlotAlreadyBookedError()
        {
            // Arrange
            _uow.Patients
                .GetPatientByUserIdAsync(_patientUserId, _ct)
                .Returns(new PatientProfile());

            _dateTimeProvider.UtcNow.Returns(_now);

            var dateTime = _now.AddDays(1).AddHours(1);
            _uow.Slots.GetByIdAsync(SlotId, _ct).Returns(new AppointmentSlot
            {
                Status = SlotStatus.Available,
                Date = DateOnly.FromDateTime(dateTime),
                StartTime = TimeOnly.FromDateTime(dateTime),
            });

            var transaction = Substitute.For<IDbContextTransaction>();
            _uow.BeginTransactionAsync().Returns(transaction);
            _uow.Slots.SetStatusFromAvailableToBookedAsync(SlotId, _ct).Returns(0);

            // Act
            var result = await _sut.BookAppointmentAsync(_patientUserId, _request, _ct);

            // Assert
            Assert.True(result.IsFailure);
            Assert.Contains(AppointmentErrors.SlotAlreadyBooked, result.Errors);
            await _uow.Received(1).BeginTransactionAsync();
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
            await _uow.Received(1).BeginTransactionAsync();
            await _uow.Slots.Received(1)
                      .SetStatusFromAvailableToBookedAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
            await _uow.Appointments.Received(1)
                      .AddAsync(Arg.Any<Appointment>(), Arg.Any<CancellationToken>());
            await _uow.Received(1).SaveChangesAsync();
            await transaction.Received(1).CommitAsync(Arg.Any<CancellationToken>());
            await transaction.DidNotReceive().RollbackAsync(Arg.Any<CancellationToken>());
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
            await transaction.DidNotReceive().RollbackAsync(Arg.Any<CancellationToken>());
        }
    }
}