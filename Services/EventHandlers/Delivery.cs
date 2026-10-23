using Domain.Entities.IdentityModule;
using Domain.Interfaces;
using Services.Abstraction.Contracts;
using Shared.DTOs.Notification;

namespace Services.EventHandlers
{
    internal static class Delivery
    {
        // Sends on one channel at most once per outbox message: a retried message skips the channels that already
        // delivered (docs/v2/06 §5). The delivery record is saved after send, together with anything send added
        // to the unit of work, so in-app notifications are exactly once. An email that went out just before a
        // crash can go out again: at least once
        public static async Task OnceAsync(IUnitOfWork uow, IClock clock, long outboxMessageId, string channel,
            Func<Task> send, CancellationToken ct)
        {
            if (await uow.MessageDeliveries.WasDeliveredAsync(outboxMessageId, channel, ct))
                return;

            await send();
            uow.MessageDeliveries.Record(outboxMessageId, channel, clock.UtcNow);
            await uow.SaveChangesAsync(ct);
        }

        // Adds in-app notifications without saving, so OnceAsync saves them with their delivery record
        public static Task AddNotificationsAsync(IUnitOfWork uow, IClock clock, CancellationToken ct, params CreateNotificationRequest[] notifications)
            => uow.Notifications.AddRangeAsync(notifications.Select(n => new Notification
            {
                UserId = n.UserId,
                Title = n.Title,
                Message = n.Message,
                Type = n.Type,
                RelatedEntityId = n.RelatedEntityId,
                CreatedAt = clock.UtcNow,
            }), ct);
    }
}
