using Domain.Entities.Messaging;
using Domain.Enums;
using Shared.DTOs.Notification;

namespace Services.EventHandlers
{
    public class AppointmentMarkedNoShowHandler(IUnitOfWork uow, IClock clock, ILogger<AppointmentMarkedNoShowHandler> logger)
        : IDomainEventHandler<AppointmentMarkedNoShow>
    {
        public async Task HandleAsync(AppointmentMarkedNoShow domainEvent, long outboxMessageId, CancellationToken ct)
        {
            var appointment = await uow.Appointments.GetByIdAsync(domainEvent.AppointmentId, ct);
            if (appointment is null)
            {
                logger.LogWarning("Appointment {AppointmentId} of outbox message {MessageId} no longer exists", domainEvent.AppointmentId, outboxMessageId);
                return;
            }

            await Delivery.OnceAsync(uow, clock, outboxMessageId, DeliveryChannels.InApp, () => Delivery.AddNotificationsAsync(uow, clock, ct,
                new CreateNotificationRequest(appointment.Patient.UserId, "Missed Appointment",
                    $"Your appointment on {appointment.Slot.Date} at {appointment.Slot.StartTime:HH:mm} was marked as missed.",
                    NotificationType.SystemAlert, appointment.Id)), ct);
        }
    }
}
