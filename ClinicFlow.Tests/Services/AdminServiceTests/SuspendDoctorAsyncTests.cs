using Domain.Entities.AppModule;
using Domain.Entities.IdentityModule;
using Domain.Enums;
using NSubstitute;
using Domain.Events;
using Shared.Errors;

namespace ClinicFlow.Tests.Services.AdminServiceTests
{
    public class SuspendDoctorAsyncTests : AdminServiceTestsBase
    {
        private const int _doctorId = 5;
        private readonly Guid _doctorUserId = Guid.NewGuid();
        private readonly CancellationToken _ct = CancellationToken.None;
        private readonly DoctorProfile _doctor;

        public SuspendDoctorAsyncTests()
        {
            _doctor = new DoctorProfile
            {
                Id = _doctorId,
                UserId = _doctorUserId,
                IsApprovedByAdmin = true,
                User = new ApplicationUser { Id = _doctorUserId, IsActive = true }
            };
            _uow.Doctors.GetByIdAsync(_doctorId, _ct).Returns(_doctor);
        }

        [Fact]
        public async Task SuspendDoctorAsync_WhenActive_DeactivatesUserAndSaves()
        {
            var result = await _sut.SuspendDoctorAsync(_doctorId, _ct);

            Assert.True(result.IsSuccess);
            Assert.False(_doctor.User.IsActive);
            await _uow.Received(1).SaveChangesAsync(_ct);
        }

        [Fact]
        public async Task SuspendDoctorAsync_WhenActive_RevokesAllActiveRefreshTokens()
        {
            var result = await _sut.SuspendDoctorAsync(_doctorId, _ct);

            Assert.True(result.IsSuccess);
            await _uow.RefreshTokens.Received(1).RevokeAllActiveAsync(_doctorUserId, "Account suspended", null, UtcNow, _ct);
        }

        [Fact]
        public async Task SuspendDoctorAsync_WhenActive_QueuesTheEventThatNotifiesTheDoctor()
        {
            await _sut.SuspendDoctorAsync(_doctorId, _ct);

            // Added before the save, so it is saved with the suspension; DoctorSuspendedHandler creates the notification
            Received.InOrder(() =>
            {
                _uow.Outbox.Add(Arg.Is<DoctorSuspended>(e => e!.DoctorUserId == _doctorUserId));
                _uow.SaveChangesAsync(_ct);
            });
        }

        [Fact]
        public async Task SuspendDoctorAsync_WhenDoctorNotFound_ReturnsDoctorNotFoundError()
        {
            _uow.Doctors.GetByIdAsync(999, _ct).Returns((DoctorProfile?)null);

            var result = await _sut.SuspendDoctorAsync(999, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(DoctorErrors.NotFound(999), result.Errors);
            await _uow.RefreshTokens.DidNotReceive().RevokeAllActiveAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task SuspendDoctorAsync_WhenAlreadySuspended_ReturnsDoctorAlreadySuspendedError()
        {
            _doctor.User.IsActive = false;

            var result = await _sut.SuspendDoctorAsync(_doctorId, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(AdminErrors.DoctorAlreadySuspended, result.Errors);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
            await _uow.RefreshTokens.DidNotReceive().RevokeAllActiveAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
            _uow.Outbox.DidNotReceive().Add(Arg.Any<IDomainEvent>());
        }
    }
}
