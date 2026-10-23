using Domain.Entities.IdentityModule;
using Domain.Enums;
using NSubstitute;
using Shared.DTOs.Notification;

namespace ClinicFlow.Tests.Services.NotificationServiceTests
{
    public class CreateAsyncTests : NotificationServiceTestsBase
    {
        private readonly CancellationToken _ct = new CancellationTokenSource().Token;

        [Fact]
        public async Task CreateAsync_Always_AddsUnreadNotificationFromRequestAndSavesOnce()
        {
            var userId = Guid.NewGuid();
            Notification? added = null;
            await _uow.Notifications.AddAsync(Arg.Do<Notification>(n => added = n), Arg.Any<CancellationToken>());

            await _sut.CreateAsync(new CreateNotificationRequest(
                userId, "Appointment confirmed", "See you on Sunday", NotificationType.AppointmentConfirmed, 15), _ct);

            Assert.NotNull(added);
            Assert.Equal(userId, added!.UserId);
            Assert.Equal("Appointment confirmed", added.Title);
            Assert.Equal("See you on Sunday", added.Message);
            Assert.Equal(NotificationType.AppointmentConfirmed, added.Type);
            Assert.Equal(15, added.RelatedEntityId);
            Assert.False(added.IsRead);
            Assert.Equal(UtcNow, added.CreatedAt); // from IClock, not a default on the entity
            await _uow.Notifications.Received(1).AddAsync(Arg.Any<Notification>(), _ct);
            await _uow.Received(1).SaveChangesAsync(_ct);
        }

        [Fact]
        public async Task CreateAsync_WhenNoRelatedEntity_LeavesRelatedEntityIdNull()
        {
            Notification? added = null;
            await _uow.Notifications.AddAsync(Arg.Do<Notification>(n => added = n), Arg.Any<CancellationToken>());

            await _sut.CreateAsync(new CreateNotificationRequest(
                Guid.NewGuid(), "Welcome", "Your account was approved", NotificationType.SystemAlert), _ct);

            Assert.NotNull(added);
            Assert.Null(added!.RelatedEntityId);
        }
    }
}
