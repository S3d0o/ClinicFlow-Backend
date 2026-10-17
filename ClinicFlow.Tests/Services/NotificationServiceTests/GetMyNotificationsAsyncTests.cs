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

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GetMyNotificationsAsync_Always_QueriesTheCallersInboxWithTheUnreadFilter(bool unreadOnly)
        {
            _uow.Notifications.GetByUserIdAsync(_userId, unreadOnly, _ct).Returns(new List<Notification>());

            var result = await _sut.GetMyNotificationsAsync(_userId, unreadOnly, _ct);

            Assert.True(result.IsSuccess);
            await _uow.Notifications.Received(1).GetByUserIdAsync(_userId, unreadOnly, _ct);
        }

        [Fact]
        public async Task GetMyNotificationsAsync_WhenNotificationsExist_MapsEveryField()
        {
            var createdAt = new DateTime(2026, 10, 16, 9, 30, 0, DateTimeKind.Utc);
            _uow.Notifications.GetByUserIdAsync(_userId, false, _ct).Returns(new List<Notification>
            {
                new()
                {
                    Id = 7, UserId = _userId, Title = "Appointment cancelled", Message = "Your visit on Sunday was cancelled",
                    Type = NotificationType.AppointmentCancelled, IsRead = true, CreatedAt = createdAt, RelatedEntityId = 15
                }
            });

            var result = await _sut.GetMyNotificationsAsync(_userId, false, _ct);

            Assert.True(result.IsSuccess);
            var response = Assert.Single(result.Value);
            Assert.Equal(new NotificationResponse
            {
                Id = 7, Title = "Appointment cancelled", Message = "Your visit on Sunday was cancelled",
                Type = NotificationType.AppointmentCancelled, IsRead = true, CreatedAt = createdAt, RelatedEntityId = 15
            }, response);
        }

        [Fact]
        public async Task GetMyNotificationsAsync_WhenInboxEmpty_ReturnsEmptyList()
        {
            _uow.Notifications.GetByUserIdAsync(_userId, false, _ct).Returns(new List<Notification>());

            var result = await _sut.GetMyNotificationsAsync(_userId, false, _ct);

            Assert.True(result.IsSuccess);
            Assert.Empty(result.Value);
        }
    }
}
