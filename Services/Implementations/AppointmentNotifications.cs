using Shared.DTOs.Notification;

namespace Services.Implementations
{
    // Shared by the appointment command services until the outbox replaces it (P0 PR 9)
    internal static class AppointmentNotifications
    {
        // Runs after the appointment change is committed: a notification failure is logged
        // and must not turn the successful operation into an error response
        public static async Task TryNotifyAsync(this INotificationService notificationService, ILogger logger,
            int appointmentId, IEnumerable<CreateNotificationRequest> notifications, CancellationToken ct)
        {
            try
            {
                await notificationService.CreateRangeAsync(notifications, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create notifications for appointment {AppointmentId}", appointmentId);
            }
        }
    }
}
