using NSubstitute;

namespace ClinicFlow.Tests.Services.NotificationServiceTests
{
    public class MarkAllAsReadAsyncTests : NotificationServiceTestsBase
    {
        private readonly CancellationToken _ct = new CancellationTokenSource().Token;

        [Fact]
        public async Task MarkAllAsReadAsync_Always_MarksOnlyTheCallersNotificationsWithoutSaveChanges()
        {
            var userId = Guid.NewGuid();

            var result = await _sut.MarkAllAsReadAsync(userId, _ct);

            Assert.True(result.IsSuccess);
            await _uow.Notifications.Received(1).MarkAllAsReadAsync(userId, _ct);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }
}
