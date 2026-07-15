using Domain.Enums;

namespace Shared.DTOs.Notification
{
    public record NotificationResponse
    {
        public int Id { get; init; }
        public string Title { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public NotificationType Type { get; init; }
        public bool IsRead { get; init; }
        public DateTime CreatedAt { get; init; }
        public int? RelatedEntityId { get; init; }
    }
}
