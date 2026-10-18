using Domain.Entities.AppModule;
using NSubstitute;
using Shared.DTOs.Appointment;
using Shared.Errors;

namespace ClinicFlow.Tests.Services.AppointmentServiceTests
{
    public class GetAppointmentAsyncTests : AppointmentServiceTestsBase
    {
        private const int _appointmentId = 1;
        private readonly CancellationToken _ct = CancellationToken.None;
        private readonly Guid _patientUserId = Guid.NewGuid();
        private readonly Guid _doctorUserId = Guid.NewGuid();

        public GetAppointmentAsyncTests()
        {
            _uow.Appointments.GetDetailedByIdAsync(_appointmentId, _ct).Returns(new Appointment
            {
                Id = _appointmentId,
                Patient = new PatientProfile { UserId = _patientUserId },
                Doctor = new DoctorProfile { UserId = _doctorUserId }
            });
            _mapper.Map<AppointmentResponse>(Arg.Any<Appointment>())
                .Returns(new AppointmentResponse { Id = _appointmentId });
        }

        [Fact]
        public async Task GetAppointmentAsync_WhenAppointmentNotFound_ReturnsNotFoundError()
        {
            var result = await _sut.GetAppointmentAsync(999, _patientUserId, "Patient", _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AppointmentErrors.NotFound(999), result.Errors);
        }

        [Fact]
        public async Task GetAppointmentAsync_WhenOwningPatient_ReturnsAppointment()
        {
            var result = await _sut.GetAppointmentAsync(_appointmentId, _patientUserId, "Patient", _ct);

            Assert.True(result.IsSuccess);
            Assert.Equal(_appointmentId, result.Value.Id);
        }

        [Fact]
        public async Task GetAppointmentAsync_WhenOwningDoctor_ReturnsAppointment()
        {
            var result = await _sut.GetAppointmentAsync(_appointmentId, _doctorUserId, "Doctor", _ct);

            Assert.True(result.IsSuccess);
        }

        [Fact]
        public async Task GetAppointmentAsync_WhenAdmin_ReturnsAppointment()
        {
            var result = await _sut.GetAppointmentAsync(_appointmentId, Guid.NewGuid(), "Admin", _ct);

            Assert.True(result.IsSuccess);
        }

        [Theory]
        [InlineData("Patient")]
        [InlineData("Doctor")]
        [InlineData("")]
        public async Task GetAppointmentAsync_WhenCallerDoesNotOwnAppointment_ReturnsForbiddenError(string role)
        {
            var result = await _sut.GetAppointmentAsync(_appointmentId, Guid.NewGuid(), role, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AppointmentErrors.Forbidden, result.Errors);
            _mapper.DidNotReceive().Map<AppointmentResponse>(Arg.Any<Appointment>());
        }
    }
}
