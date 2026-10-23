using Domain.Entities.IdentityModule;
using Domain.Enums;
using NSubstitute;
using Shared.DTOs.Notification;

namespace ClinicFlow.Tests.Services.NotificationServiceTests
{
    public class CreateRangeAsyncTests : NotificationServiceTestsBase
    {
        private readonly CancellationToken _ct = new CancellationTokenSource().Token;

        [Fact]
        public async Task CreateRangeAsync_Always_AddsEveryNotificationInOneSave()
        {
            var patientId = Guid.NewGuid();
            var doctorId = Guid.NewGuid();
            List<Notification>? added = null;
            await _uow.Notifications.AddRangeAsync(
                Arg.Do<IEnumerable<Notification>>(n => added = n.ToList()), Arg.Any<CancellationToken>());

            await _sut.CreateRangeAsync(new[]
            {
                new CreateNotificationRequest(patientId, "Cancelled", "Your visit was cancelled", NotificationType.AppointmentCancelled, 15),
                new CreateNotificationRequest(doctorId, "Cancelled", "A visit was cancelled", NotificationType.AppointmentCancelled, 15)
            }, _ct);

            Assert.NotNull(added);
            Assert.Equal(new[] { patientId, doctorId }, added!.Select(n => n.UserId));
            Assert.All(added, n =>
            {
                Assert.Equal(NotificationType.AppointmentCancelled, n.Type);
                Assert.Equal(15, n.RelatedEntityId);
                Assert.False(n.IsRead);
                Assert.Equal(UtcNow, n.CreatedAt);
            });
            await _uow.Received(1).SaveChangesAsync(_ct);
        }

        [Fact]
        public async Task CreateRangeAsync_WhenEmpty_SavesNothingNew()
        {
            List<Notification>? added = null;
            await _uow.Notifications.AddRangeAsync(
                Arg.Do<IEnumerable<Notification>>(n => added = n.ToList()), Arg.Any<CancellationToken>());

            await _sut.CreateRangeAsync(Array.Empty<CreateNotificationRequest>(), _ct);

            Assert.NotNull(added);
            Assert.Empty(added!);
        }
    }
}
