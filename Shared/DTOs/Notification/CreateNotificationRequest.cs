using Domain.Enums;

namespace Shared.DTOs.Notification
{
    public record CreateNotificationRequest(
    Guid UserId,
    string Title,
    string Message,
    NotificationType Type,
    int? RelatedEntityId = null);
}
