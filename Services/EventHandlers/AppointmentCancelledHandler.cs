using Domain.Entities.Messaging;
using Domain.Enums;
using Shared.DTOs.Notification;

namespace Services.EventHandlers
{
    public class AppointmentCancelledHandler(IUnitOfWork uow, IClock clock, ILogger<AppointmentCancelledHandler> logger)
        : IDomainEventHandler<AppointmentCancelled>
    {
        public async Task HandleAsync(AppointmentCancelled domainEvent, long outboxMessageId, CancellationToken ct)
        {
            var appointment = await uow.Appointments.GetByIdAsync(domainEvent.AppointmentId, ct);
            if (appointment is null)
            {
                logger.LogWarning("Appointment {AppointmentId} of outbox message {MessageId} no longer exists", domainEvent.AppointmentId, outboxMessageId);
                return;
            }
            var when = $"on {appointment.Slot.Date} at {appointment.Slot.StartTime:HH:mm}";

            await Delivery.OnceAsync(uow, clock, outboxMessageId, DeliveryChannels.InApp, () => Delivery.AddNotificationsAsync(uow, clock, ct,
                new CreateNotificationRequest(appointment.Patient.UserId, "Appointment Cancelled",
                    $"Your appointment {when} has been cancelled.",
                    NotificationType.AppointmentCancelled, appointment.Id),
                new CreateNotificationRequest(appointment.Doctor.UserId, "Appointment Cancelled",
                    $"Your appointment with {appointment.Patient.User.FirstName} {appointment.Patient.User.LastName} {when} has been cancelled.",
                    NotificationType.AppointmentCancelled, appointment.Id)), ct);
        }
    }
}
