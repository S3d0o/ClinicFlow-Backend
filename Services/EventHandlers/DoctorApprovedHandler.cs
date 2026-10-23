using Domain.Entities.Messaging;
using Domain.Enums;
using Shared.DTOs.Notification;

namespace Services.EventHandlers
{
    public class DoctorApprovedHandler(IUnitOfWork uow, IClock clock) : IDomainEventHandler<DoctorApproved>
    {
        public Task HandleAsync(DoctorApproved domainEvent, long outboxMessageId, CancellationToken ct)
            => Delivery.OnceAsync(uow, clock, outboxMessageId, DeliveryChannels.InApp, () => Delivery.AddNotificationsAsync(uow, clock, ct,
                new CreateNotificationRequest(domainEvent.DoctorUserId, "Profile Approved",
                    "Your doctor profile has been approved. You can now set your schedule and accept appointments.",
                    NotificationType.SystemAlert)), ct);
    }
}
