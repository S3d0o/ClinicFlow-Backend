using Domain.Entities.AppModule;
using Domain.Enums;
using Domain.Parameters;
using NSubstitute;
using Shared.DTOs.Appointment;
using Shared.Errors;

namespace ClinicFlow.Tests.Services.AppointmentServiceTests
{
    public class GetPatientAppointmentsAsyncTests : AppointmentServiceTestsBase
    {
        private readonly Guid _patientUserId = Guid.NewGuid();
        private readonly CancellationToken _ct = CancellationToken.None;
        private readonly AppointmentFilterRequest _request = new()
        {
            Status = AppointmentStatus.Cancelled,
            PageNumber = 1,
            PageSize = 10
        };

        [Fact]
        public async Task GetPatientAppointmentsAsync_WhenPatientExists_QueriesOwnAppointmentsAndMapsThem()
        {
            var filterParams = new AppointmentFilterParams();
            IReadOnlyList<Appointment> appointments = [new Appointment { Id = 3 }];
            var mapped = new List<AppointmentResponse> { new() { Id = 3 } };

            _uow.Patients.GetPatientByUserIdAsync(_patientUserId, _ct).Returns(new PatientProfile { Id = 2, UserId = _patientUserId });
            _mapper.Map<AppointmentFilterParams>(_request).Returns(filterParams);
            _uow.Appointments.GetByPatientUserIdAsync(_patientUserId, AppointmentStatus.Cancelled, filterParams, _ct)
                .Returns((appointments, 1));
            _mapper.Map<List<AppointmentResponse>>(appointments).Returns(mapped);

            var result = await _sut.GetPatientAppointmentsAsync(_patientUserId, _request, _ct);

            Assert.True(result.IsSuccess);
            Assert.Same(mapped, result.Value);
            await _uow.Appointments.Received(1)
                .GetByPatientUserIdAsync(_patientUserId, AppointmentStatus.Cancelled, filterParams, _ct);
        }

        [Fact]
        public async Task GetPatientAppointmentsAsync_WhenPatientProfileNotFound_ReturnsProfileNotFoundError()
        {
            _uow.Patients.GetPatientByUserIdAsync(_patientUserId, _ct).Returns((PatientProfile?)null);

            var result = await _sut.GetPatientAppointmentsAsync(_patientUserId, _request, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(PatientErrors.ProfileNotFound(_patientUserId), result.Errors);
            await _uow.Appointments.DidNotReceive().GetByPatientUserIdAsync(
                Arg.Any<Guid>(), Arg.Any<AppointmentStatus?>(), Arg.Any<AppointmentFilterParams>(), Arg.Any<CancellationToken>());
        }
    }
}
