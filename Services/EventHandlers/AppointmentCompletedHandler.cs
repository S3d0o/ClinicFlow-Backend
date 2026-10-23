using Domain.Entities.Messaging;
using Domain.Enums;
using Shared.DTOs.Notification;

namespace Services.EventHandlers
{
    public class AppointmentCompletedHandler(IUnitOfWork uow, IClock clock, ILogger<AppointmentCompletedHandler> logger)
        : IDomainEventHandler<AppointmentCompleted>
    {
        public async Task HandleAsync(AppointmentCompleted domainEvent, long outboxMessageId, CancellationToken ct)
        {
            var appointment = await uow.Appointments.GetByIdAsync(domainEvent.AppointmentId, ct);
            if (appointment is null)
            {
                logger.LogWarning("Appointment {AppointmentId} of outbox message {MessageId} no longer exists", domainEvent.AppointmentId, outboxMessageId);
                return;
            }

            await Delivery.OnceAsync(uow, clock, outboxMessageId, DeliveryChannels.InApp, () => Delivery.AddNotificationsAsync(uow, clock, ct,
                new CreateNotificationRequest(appointment.Patient.UserId, "Appointment Completed",
                    "Your appointment has been completed. You can now leave a review.",
                    NotificationType.AppointmentCompleted, appointment.Id)), ct);
        }
    }
}
