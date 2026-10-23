using Domain.Entities.AppModule;
using Domain.Enums;
using NSubstitute;
using Shared.DTOs.Appointment;
using Shared.Errors;
using Domain.Errors;

namespace ClinicFlow.Tests.Services.AppointmentVisitServiceTests
{
    public class AddDoctorNotesAsyncTests : AppointmentVisitServiceTestsBase
    {
        private const int _appointmentId = 1;
        private const int _doctorId = 5;
        private readonly Guid _doctorUserId = Guid.NewGuid();
        private readonly CancellationToken _ct = CancellationToken.None;
        private readonly DoctorNotesRequest _request = new("Prescribed rest for a week");
        private readonly Appointment _appointment;

        public AddDoctorNotesAsyncTests()
        {
            _uow.Doctors.GetByUserIdAsync(_doctorUserId, _ct)
                .Returns(new DoctorProfile { Id = _doctorId, UserId = _doctorUserId });

            _appointment = new Appointment
            {
                Id = _appointmentId,
                DoctorProfileId = _doctorId,
            }.WithStatus(AppointmentStatus.Completed);
            _uow.Appointments.GetByIdAsync(_appointmentId, _ct).Returns(_appointment);
        }

        [Theory]
        [InlineData(AppointmentStatus.Confirmed)]
        [InlineData(AppointmentStatus.Completed)]
        public async Task AddDoctorNotesAsync_WhenStatusAllowsNotes_SavesNotes(AppointmentStatus status)
        {
            _appointment.WithStatus(status);

            var result = await _sut.AddDoctorNotesAsync(_doctorUserId, _appointmentId, _request, _ct);

            Assert.True(result.IsSuccess);
            Assert.Equal("Prescribed rest for a week", _appointment.DoctorNotes);
            await _uow.Received(1).SaveChangesAsync(_ct);
        }

        [Fact]
        public async Task AddDoctorNotesAsync_WhenDoctorProfileNotFound_ReturnsProfileNotFoundError()
        {
            var unknownUserId = Guid.NewGuid();
            _uow.Doctors.GetByUserIdAsync(unknownUserId, _ct).Returns((DoctorProfile?)null);

            var result = await _sut.AddDoctorNotesAsync(unknownUserId, _appointmentId, _request, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(DoctorErrors.ProfileNotFound(unknownUserId), result.Errors);
            await _uow.Appointments.DidNotReceive().GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task AddDoctorNotesAsync_WhenAppointmentNotFound_ReturnsNotFoundError()
        {
            _uow.Appointments.GetByIdAsync(999, _ct).Returns((Appointment?)null);

            var result = await _sut.AddDoctorNotesAsync(_doctorUserId, 999, _request, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AppointmentErrors.NotFound(999), result.Errors);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task AddDoctorNotesAsync_WhenAppointmentBelongsToAnotherDoctor_ReturnsForbiddenError()
        {
            _appointment.DoctorProfileId = _doctorId + 1;

            var result = await _sut.AddDoctorNotesAsync(_doctorUserId, _appointmentId, _request, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AppointmentErrors.Forbidden, result.Errors);
            Assert.Null(_appointment.DoctorNotes);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(AppointmentStatus.Cancelled)]
        [InlineData(AppointmentStatus.Pending)]
        public async Task AddDoctorNotesAsync_WhenCancelledOrPending_ReturnsDoctorNotesNotAllowedError(AppointmentStatus status)
        {
            _appointment.WithStatus(status);

            var result = await _sut.AddDoctorNotesAsync(_doctorUserId, _appointmentId, _request, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AppointmentErrors.DoctorNotesNotAllowed, result.Errors);
            Assert.Null(_appointment.DoctorNotes);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }
}
