using Domain.Entities.Messaging;
using Domain.Enums;
using Shared.DTOs.Notification;
using Shared.Messaging;

namespace Services.EventHandlers
{
    // The reminder email and notification. A visit cancelled since the reminder was queued gets neither
    public class AppointmentReminderDueHandler(IUnitOfWork uow, ITemplatedMessenger messenger, IClock clock, ILogger<AppointmentReminderDueHandler> logger)
        : IDomainEventHandler<AppointmentReminderDue>
    {
        public async Task HandleAsync(AppointmentReminderDue domainEvent, long outboxMessageId, CancellationToken ct)
        {
            var appointment = await uow.Appointments.GetByIdAsync(domainEvent.AppointmentId, ct);
            if (appointment is null || appointment.Status != AppointmentStatus.Confirmed)
            {
                logger.LogInformation("Reminder for appointment {AppointmentId} skipped: it is no longer confirmed", domainEvent.AppointmentId);
                return;
            }

            await Delivery.EmailPatientOnceAsync(uow, messenger, clock, logger, outboxMessageId, appointment, TemplateKeys.AppointmentReminder, ct);

            await Delivery.OnceAsync(uow, clock, outboxMessageId, DeliveryChannels.InApp, () => Delivery.AddNotificationsAsync(uow, clock, ct,
                new CreateNotificationRequest(appointment.Patient.User.Id, "Appointment Reminder",
                    $"Reminder: You have an appointment on {appointment.Slot.Date} at {appointment.Slot.StartTime:HH:mm}.",
                    NotificationType.AppointmentReminder, appointment.Id)), ct);
        }
    }
}
