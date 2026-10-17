using Domain.Entities.IdentityModule;
using NSubstitute;
using Shared.Errors;

namespace ClinicFlow.Tests.Services.NotificationServiceTests
{
    public class MarkAsReadAsyncTests : NotificationServiceTestsBase
    {
        private const int _notificationId = 42;
        private readonly Guid _ownerId = Guid.NewGuid();
        private readonly CancellationToken _ct = new CancellationTokenSource().Token;

        public MarkAsReadAsyncTests()
        {
            _uow.Notifications.GetByIdAsync(_notificationId, _ct)
                .Returns(new Notification { Id = _notificationId, UserId = _ownerId, IsRead = false });
        }

        [Fact]
        public async Task MarkAsReadAsync_WhenCallerOwnsNotification_MarksItReadWithoutSaveChanges()
        {
            var result = await _sut.MarkAsReadAsync(_ownerId, _notificationId, _ct);

            Assert.True(result.IsSuccess);
            await _uow.Notifications.Received(1).MarkAsReadAsync(_notificationId, _ct);
            // ExecuteUpdateAsync writes directly
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task MarkAsReadAsync_WhenNotificationNotFound_ReturnsNotFoundError()
        {
            var result = await _sut.MarkAsReadAsync(_ownerId, 999, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(NotificationErrors.NotFound(999), result.Errors);
            await _uow.Notifications.DidNotReceive().MarkAsReadAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task MarkAsReadAsync_WhenNotificationBelongsToAnotherUser_ReturnsUnauthorizedAndChangesNothing()
        {
            var result = await _sut.MarkAsReadAsync(Guid.NewGuid(), _notificationId, _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(NotificationErrors.Unauthorized, result.Errors);
            await _uow.Notifications.DidNotReceive().MarkAsReadAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        }
    }
}
