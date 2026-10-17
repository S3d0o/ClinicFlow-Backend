namespace Shared.DTOs.Notification
{
    public record NotificationFilterRequest
    {
        public bool UnreadOnly { get; init; }
        public int PageNumber { get; init; } = 1;
        // Newest first; a page used to hold every notification the user ever had (REVIEW.md NL2)
        public int PageSize { get; init; } = 50;
    }
}
