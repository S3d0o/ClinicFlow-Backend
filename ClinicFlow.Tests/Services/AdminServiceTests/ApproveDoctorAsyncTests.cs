using Domain.Entities.AppModule;
using Domain.Enums;
using NSubstitute;
using Domain.Events;
using Shared.Errors;

namespace ClinicFlow.Tests.Services.AdminServiceTests
{
    public class ApproveDoctorAsyncTests : AdminServiceTestsBase
    {
        private const int _doctorId = 5;
        private readonly Guid _doctorUserId = Guid.NewGuid();
        private readonly CancellationToken _ct = CancellationToken.None;
        private readonly DoctorProfile _doctor;

        public ApproveDoctorAsyncTests()
        {
            _doctor = new DoctorProfile { Id = _doctorId, UserId = _doctorUserId, IsApprovedByAdmin = false };
            _uow.Doctors.GetByIdAsync(_doctorId, _ct).Returns(_doctor);
        }

        [Fact]
        public async Task ApproveDoctorAsync_WhenPending_ApprovesAndSaves()
        {
            var result = await _sut.ApproveDoctorAsync(_doctorId, _ct);

            Assert.True(result.IsSuccess);
            Assert.True(_doctor.IsApprovedByAdmin);
            await _uow.Received(1).SaveChangesAsync(_ct);
        }

        [Fact]
        public async Task ApproveDoctorAsync_WhenPending_QueuesTheEventThatNotifiesTheDoctor()
        {
            await _sut.ApproveDoctorAsync(_doctorId, _ct);

            // Added before the save, so it is saved with the approval; DoctorApprovedHandler creates the notification
            Received.InOrder(() =>
            {
                _uow.Outbox.Add(Arg.Is<DoctorApproved>(e => e!.DoctorUserId == _doctorUserId));
                _uow.SaveChangesAsync(_ct);
            });
        }

        [Fact]
        public async Task ApproveDoctorAsync_WhenDoctorNotFound_ReturnsDoctorNotFoundError()
        {
            _uow.Doctors.GetByIdAsync(999, _ct).Returns((DoctorProfile?)null);

            var result = await _sut.ApproveDoctorAsync(999, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(DoctorErrors.NotFound(999), result.Errors);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ApproveDoctorAsync_WhenAlreadyApproved_ReturnsDoctorAlreadyApprovedError()
        {
            _doctor.IsApprovedByAdmin = true;

            var result = await _sut.ApproveDoctorAsync(_doctorId, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AdminErrors.DoctorAlreadyApproved, result.Errors);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
            _uow.Outbox.DidNotReceive().Add(Arg.Any<IDomainEvent>());
        }
    }
}
