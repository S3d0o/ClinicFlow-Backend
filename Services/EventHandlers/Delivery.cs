using Domain.Entities.IdentityModule;
using Domain.Entities.Messaging;
using Domain.Interfaces;
using Services.Abstraction.Contracts;
using Services.Messaging;
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

        // The patient's email about a visit, from the template in their language. Skipped, with no delivery recorded,
        // for a patient who doesn't take email (ApplicationUser.AcceptsEmail)
        public static async Task EmailPatientOnceAsync(
            IUnitOfWork uow, ITemplatedMessenger messenger, IClock clock, ILogger logger,
            long outboxMessageId, Appointment appointment, string templateKey, CancellationToken ct)
        {
            var patient = appointment.Patient.User;
            if (!patient.AcceptsEmail())
            {
                logger.LogInformation(
                    "{TemplateKey} email for appointment {AppointmentId} skipped: the patient has no confirmed email, or takes phone messages only",
                    templateKey, appointment.Id);
                return;
            }

            await OnceAsync(uow, clock, outboxMessageId, DeliveryChannels.Email, async () =>
            {
                var branch = await uow.Branches.GetByIdAsync(appointment.BranchId, ct);
                await messenger.SendEmailAsync(
                    patient, templateKey, MessageValues.Appointment(appointment, branch, patient.PreferredLanguage), ct);
            }, ct);
        }
    }
}
