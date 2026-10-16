using Domain.Entities.AppModule;
using Domain.Enums;
using Domain.Parameters;
using NSubstitute;
using Shared.DTOs.Appointment;
using Shared.Errors;

namespace ClinicFlow.Tests.Services.AppointmentServiceTests
{
    public class GetDoctorAppointmentsAsyncTests : AppointmentServiceTestsBase
    {
        private readonly Guid _doctorUserId = Guid.NewGuid();
        private readonly CancellationToken _ct = CancellationToken.None;
        private readonly AppointmentFilterRequest _request = new()
        {
            Status = AppointmentStatus.Confirmed,
            PageNumber = 2,
            PageSize = 5
        };

        [Fact]
        public async Task GetDoctorAppointmentsAsync_WhenDoctorExists_QueriesOwnAppointmentsAndMapsThem()
        {
            var filterParams = new AppointmentFilterParams();
            IReadOnlyList<Appointment> appointments = [new Appointment { Id = 1 }, new Appointment { Id = 2 }];
            var mapped = new List<AppointmentResponse> { new() { Id = 1 }, new() { Id = 2 } };

            _uow.Doctors.GetByUserIdAsync(_doctorUserId, _ct).Returns(new DoctorProfile { Id = 5, UserId = _doctorUserId });
            _mapper.Map<AppointmentFilterParams>(_request).Returns(filterParams);
            _uow.Appointments.GetDoctorsAppointmentsAsync(_doctorUserId, AppointmentStatus.Confirmed, filterParams, _ct)
                .Returns((appointments, 2));
            _mapper.Map<List<AppointmentResponse>>(appointments).Returns(mapped);

            var result = await _sut.GetDoctorAppointmentsAsync(_doctorUserId, _request, _ct);

            Assert.True(result.IsSuccess);
            Assert.Same(mapped, result.Value);
            await _uow.Appointments.Received(1)
                .GetDoctorsAppointmentsAsync(_doctorUserId, AppointmentStatus.Confirmed, filterParams, _ct);
        }

        [Fact]
        public async Task GetDoctorAppointmentsAsync_WhenDoctorProfileNotFound_ReturnsProfileNotFoundError()
        {
            _uow.Doctors.GetByUserIdAsync(_doctorUserId, _ct).Returns((DoctorProfile?)null);

            var result = await _sut.GetDoctorAppointmentsAsync(_doctorUserId, _request, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(DoctorErrors.ProfileNotFound(_doctorUserId), result.Errors);
            await _uow.Appointments.DidNotReceive().GetDoctorsAppointmentsAsync(
                Arg.Any<Guid>(), Arg.Any<AppointmentStatus?>(), Arg.Any<AppointmentFilterParams>(), Arg.Any<CancellationToken>());
        }
    }
}
