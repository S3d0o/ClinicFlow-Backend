using Domain.Entities.AppModule;
using Domain.Entities.IdentityModule;
using Domain.Enums;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shared.DTOs.Notification;
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
            await _uow.RefreshTokens.Received(1).RevokeAllActiveAsync(_doctorUserId, "Account suspended", null, _ct);
        }

        [Fact]
        public async Task SuspendDoctorAsync_WhenActive_NotifiesDoctor()
        {
            await _sut.SuspendDoctorAsync(_doctorId, _ct);

            await _notificationService.Received(1).CreateAsync(
                Arg.Is<CreateNotificationRequest>(n => n!.UserId == _doctorUserId && n.Type == NotificationType.SystemAlert),
                _ct);
        }

        [Fact]
        public async Task SuspendDoctorAsync_WhenNotificationFails_StillReturnsSuccessAndRevokesTokens()
        {
            _notificationService.CreateAsync(Arg.Any<CreateNotificationRequest>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new InvalidOperationException("notification store down"));

            var result = await _sut.SuspendDoctorAsync(_doctorId, _ct);

            Assert.True(result.IsSuccess);
            Assert.False(_doctor.User.IsActive);
            await _uow.RefreshTokens.Received(1).RevokeAllActiveAsync(_doctorUserId, "Account suspended", null, _ct);
        }

        [Fact]
        public async Task SuspendDoctorAsync_WhenDoctorNotFound_ReturnsDoctorNotFoundError()
        {
            _uow.Doctors.GetByIdAsync(999, _ct).Returns((DoctorProfile?)null);

            var result = await _sut.SuspendDoctorAsync(999, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(DoctorErrors.NotFound(999), result.Errors);
            await _uow.RefreshTokens.DidNotReceive().RevokeAllActiveAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
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
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
            await _notificationService.DidNotReceive()
                .CreateAsync(Arg.Any<CreateNotificationRequest>(), Arg.Any<CancellationToken>());
        }
    }
}
