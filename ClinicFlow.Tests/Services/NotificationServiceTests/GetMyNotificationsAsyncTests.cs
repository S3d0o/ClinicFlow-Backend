using Domain.Entities.IdentityModule;
using Domain.Enums;
using NSubstitute;
using Shared.DTOs.Notification;

namespace ClinicFlow.Tests.Services.NotificationServiceTests
{
    public class GetMyNotificationsAsyncTests : NotificationServiceTestsBase
    {
        private readonly Guid _userId = Guid.NewGuid();
        private readonly CancellationToken _ct = new CancellationTokenSource().Token;

        private void InboxReturns(List<Notification> page, int totalCount)
            => _uow.Notifications.GetByUserIdAsync(_userId, Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<int>(), _ct)
                .Returns((page, totalCount));

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GetMyNotificationsAsync_Always_QueriesTheCallersInboxWithTheFilterAndPage(bool unreadOnly)
        {
            InboxReturns([], 0);

            var result = await _sut.GetMyNotificationsAsync(_userId, new NotificationFilterRequest { UnreadOnly = unreadOnly, PageNumber = 3, PageSize = 20 }, _ct);

            Assert.True(result.IsSuccess);
            await _uow.Notifications.Received(1).GetByUserIdAsync(_userId, unreadOnly, 3, 20, _ct);
        }

        [Fact]
        public async Task GetMyNotificationsAsync_Defaults_AreTheFirstPageOf50()
        {
            // REVIEW.md NL2: the inbox used to return every notification the user ever had
            InboxReturns([], 0);

            await _sut.GetMyNotificationsAsync(_userId, new NotificationFilterRequest(), _ct);

            await _uow.Notifications.Received(1).GetByUserIdAsync(_userId, false, 1, 50, _ct);
        }

        [Fact]
        public async Task GetMyNotificationsAsync_WhenNotificationsExist_MapsEveryFieldAndReturnsTheTotal()
        {
            var createdAt = new DateTime(2026, 10, 16, 9, 30, 0, DateTimeKind.Utc);
            InboxReturns(
            [
                new()
                {
                    Id = 7, UserId = _userId, Title = "Appointment cancelled", Message = "Your visit on Sunday was cancelled",
                    Type = NotificationType.AppointmentCancelled, IsRead = true, CreatedAt = createdAt, RelatedEntityId = 15
                }
            ], totalCount: 61);

            var result = await _sut.GetMyNotificationsAsync(_userId, new NotificationFilterRequest(), _ct);

            Assert.True(result.IsSuccess);
            Assert.Equal(61, result.Value.TotalCount);
            var response = Assert.Single(result.Value.Items);
            Assert.Equal(new NotificationResponse
            {
                Id = 7, Title = "Appointment cancelled", Message = "Your visit on Sunday was cancelled",
                Type = NotificationType.AppointmentCancelled, IsRead = true, CreatedAt = createdAt, RelatedEntityId = 15
            }, response);
        }

        [Fact]
        public async Task GetMyNotificationsAsync_WhenInboxEmpty_ReturnsEmptyPage()
        {
            InboxReturns([], 0);

            var result = await _sut.GetMyNotificationsAsync(_userId, new NotificationFilterRequest(), _ct);

            Assert.True(result.IsSuccess);
            Assert.Empty(result.Value.Items);
            Assert.Equal(0, result.Value.TotalCount);
        }
    }
}
