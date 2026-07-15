using ClinicFlow.Domain.Enums;
using Shared.DTOs.Notification;

namespace Services.Abstraction.Contracts
{
    public interface INotificationService
    {
        // User reads their own notifications
        Task<Result<List<NotificationResponse>>> GetMyNotificationsAsync(
            Guid userId, bool unreadOnly = false, CancellationToken ct = default);

        // Mark one notification as read
        Task<Result> MarkAsReadAsync(Guid userId, int notificationId, CancellationToken ct);

        // Mark all notifications as read
        Task<Result> MarkAllAsReadAsync(Guid userId, CancellationToken ct);

        // Called internally by AppointmentService — not exposed via HTTP
        Task CreateAsync(CreateNotificationRequest request, CancellationToken ct = default);

        // Called internally when multiple notifications needed (cancel → notify patient + doctor)
        Task CreateRangeAsync(IEnumerable<CreateNotificationRequest> notifications, CancellationToken ct);
    }
}
