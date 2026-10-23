using Domain.Entities.Messaging;
using Domain.Enums;
using Shared.DTOs.Notification;

namespace Services.EventHandlers
{
    public class DoctorSuspendedHandler(IUnitOfWork uow, IClock clock) : IDomainEventHandler<DoctorSuspended>
    {
        public Task HandleAsync(DoctorSuspended domainEvent, long outboxMessageId, CancellationToken ct)
            => Delivery.OnceAsync(uow, clock, outboxMessageId, DeliveryChannels.InApp, () => Delivery.AddNotificationsAsync(uow, clock, ct,
                new CreateNotificationRequest(domainEvent.DoctorUserId, "Profile Suspended",
                    "Your doctor profile has been suspended. Please contact support for more information.",
                    NotificationType.SystemAlert)), ct);
    }
}
