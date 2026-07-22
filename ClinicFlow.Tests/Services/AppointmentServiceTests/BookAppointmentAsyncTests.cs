using AutoMapper;
using ClinicFlow.Domain.Enums;
using Domain.Entities.AppModule;
using Domain.Interfaces;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Services.Abstraction.Contracts;
using Services.Implementations;
using Shared.DTOs.Appointment;
using Shared.Errors;

namespace ClinicFlow.Tests.Services.AppointmentServiceTests
{
    public class BookAppointmentAsyncTests : AppointmentServiceTestsBase
    {
        

        [Fact]
        public async Task BookAppointmentAsync_WhenPatientProfileNotFound_ReturnsProfileNotFoundError()
        {
            // Arrange
            var patientUserId = Guid.NewGuid();
            var ct = CancellationToken.None;
            var request = new BookAppointmentRequest(3, "no reason");
            _uow.Patients.GetPatientByUserIdAsync(patientUserId, ct).Returns((PatientProfile?)null);
            var error = PatientErrors.ProfileNotFound(patientUserId);


            // Act
            var result = await _sut.BookAppointmentAsync(patientUserId, request, ct);


            // Assert
            Assert.NotNull(result);
            Assert.True(result.IsFailure);
            Assert.Contains(error, result.Errors);
            await _uow.Slots.DidNotReceive().GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task BookAppointmentAsync_WhenSlotNotFound_ReturnsNotFoundError()
        {
            //Arrange

            var patientUserId = Guid.NewGuid();
            var ct = CancellationToken.None;
            int slotId = 1;
            _uow.Patients.GetPatientByUserIdAsync(patientUserId, ct).Returns(new PatientProfile());
            _uow.Slots.GetByIdAsync(slotId, ct).Returns((AppointmentSlot?)null);
            var request = new BookAppointmentRequest(slotId, "no reason");
            var error = AppointmentErrors.NotFound(slotId);

            //Act
            var result = await _sut.BookAppointmentAsync(patientUserId, request, ct);

            //Assert
            Assert.True(result.IsFailure);
            Assert.Contains(error, result.Errors);
            await _uow.Slots.DidNotReceive().SetStatusFromAvailableToBookedAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());

        }

        [Theory]
        [InlineData(SlotStatus.Booked)]
        [InlineData(SlotStatus.Blocked)]
        public async Task BookAppointmentAsync_WhenSlotIsNotAvailable_ReturnsSlotNotAvailableError(SlotStatus status)
        {
            //Arrange
            var patientUserId = Guid.NewGuid();
            var ct = CancellationToken.None;
            int slotId = 1;
            _uow.Patients.GetPatientByUserIdAsync(patientUserId, ct).Returns(new PatientProfile());
            _uow.Slots.GetByIdAsync(slotId, ct).Returns(new AppointmentSlot
            {
                Status = status
            });
            var error = AppointmentErrors.SlotNotAvailable;

            var request = new BookAppointmentRequest(slotId, "no reason");

            //Act
            var result = await _sut.BookAppointmentAsync(patientUserId, request, ct);

            //Assert
            Assert.True(result.IsFailure);
            Assert.Contains(error, result.Errors);
            await _uow.Slots.DidNotReceive().SetStatusFromAvailableToBookedAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(-1, 0)]   // yesterday, same time
        [InlineData(0, -1)]   // today, 1 hour ago
        public async Task BookAppointmentAsync_WhenSlotIsInPast_ReturnsSlotInPastError(int daysOffset, int hoursOffset)
        {
            //Arrange
            var patientUserId = Guid.NewGuid();
            var ct = CancellationToken.None;
            int slotId = 1;
            _uow.Patients.GetPatientByUserIdAsync(patientUserId, ct).Returns(new PatientProfile());

            var now = new DateTime(2026, 6, 15, 10, 0, 0);
            _dateTimeProvider.UtcNow.Returns(now);

            // Slot is built RELATIVE to now — immediately obvious it's in the past
            var slotDateTime = now.AddDays(daysOffset).AddHours(hoursOffset);

            _uow.Slots.GetByIdAsync(slotId, ct).Returns(new AppointmentSlot
            {
                Status = SlotStatus.Available,
                Date = DateOnly.FromDateTime(slotDateTime),
                StartTime = TimeOnly.FromDateTime(slotDateTime),
            });
            var request = new BookAppointmentRequest(slotId, "no reason");
            var error = AppointmentErrors.SlotInPast;

            //Act

            var result = await _sut.BookAppointmentAsync(patientUserId, request, ct);

            //Assert
            Assert.True(result.IsFailure);
            Assert.Contains(error, result.Errors);
            await _uow.Slots.DidNotReceive().SetStatusFromAvailableToBookedAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());

        }

        [Fact]
        public async Task BookAppointmentAsync_WhenSlotIsAlreadyBooked_ReturnsSlotAlreadyBookedError()
        {
            //Arrange
            var patientUserId = Guid.NewGuid();
            var ct = CancellationToken.None;
            int slotId = 1;
            _uow.Patients.GetPatientByUserIdAsync(patientUserId, ct).Returns(new PatientProfile());

            var now = new DateTime(2026, 6, 15, 10, 0, 0);
            _dateTimeProvider.UtcNow.Returns(now);

            var dateTime = now.AddDays(1).AddHours(1);

            _uow.Slots.GetByIdAsync(slotId, ct).Returns(new AppointmentSlot
            {
                Status = SlotStatus.Available,
                Date = DateOnly.FromDateTime(dateTime),
                StartTime = TimeOnly.FromDateTime(dateTime),
            });
            var request = new BookAppointmentRequest(slotId, "no reason");
            var error = AppointmentErrors.SlotAlreadyBooked;
            var transaction = Substitute.For<IDbContextTransaction>();
            _uow.BeginTransactionAsync().Returns(transaction);
            _uow.Slots.SetStatusFromAvailableToBookedAsync(slotId, ct).Returns(0);

            //Act

            var result = await _sut.BookAppointmentAsync(patientUserId, request, ct);

            //Assert
            Assert.True(result.IsFailure);
            Assert.Contains(error, result.Errors);
            await _uow.Received(1).BeginTransactionAsync();
            await _uow.Appointments.DidNotReceive().AddAsync(Arg.Any<Appointment>(), Arg.Any<CancellationToken>());

        }

        [Fact]
        public async Task BookAppointmentAsync_WhenAllInputsAreValid_ReturnsSuccess()
        {
            //Arrange
            var patientUserId = Guid.NewGuid();
            var ct = CancellationToken.None;
            int slotId = 1;
            _uow.Patients.GetPatientByUserIdAsync(patientUserId, ct).Returns(new PatientProfile());

            var now = new DateTime(2026, 6, 15, 10, 0, 0);
            _dateTimeProvider.UtcNow.Returns(now);

            var dateTime = now.AddDays(1).AddHours(1);

            _uow.Slots.GetByIdAsync(slotId, ct).Returns(new AppointmentSlot
            {
                Id = slotId,
                Status = SlotStatus.Available,
                Date = DateOnly.FromDateTime(dateTime),
                StartTime = TimeOnly.FromDateTime(dateTime),
            });
            var request = new BookAppointmentRequest(slotId, "no reason");
            var transaction = Substitute.For<IDbContextTransaction>();
            _uow.BeginTransactionAsync().Returns(transaction);
            _uow.Slots.SetStatusFromAvailableToBookedAsync(slotId, ct).Returns(1);

            var appointmentResponse = new AppointmentResponse
            {
                SlotId = slotId,
                AppointmentDate = DateOnly.FromDateTime(dateTime),
                StartTime = TimeOnly.FromDateTime(dateTime)
            };
            _mapper.Map<AppointmentResponse>(Arg.Any<Appointment>()).Returns(appointmentResponse);

            //Act

            var result = await _sut.BookAppointmentAsync(patientUserId, request, ct);

            //Assert
            await _uow.Received(1).BeginTransactionAsync();
            await _uow.Slots.Received(1).SetStatusFromAvailableToBookedAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
            await _uow.Appointments.Received(1).AddAsync(Arg.Any<Appointment>(), Arg.Any<CancellationToken>());
            await _uow.Received(1).SaveChangesAsync();
            await transaction.Received(1).CommitAsync(Arg.Any<CancellationToken>());
            await transaction.DidNotReceive().RollbackAsync(Arg.Any<CancellationToken>());
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal(appointmentResponse, result.Value);

        }
    }
}