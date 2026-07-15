namespace Domain.Entities.IdentityModule
{
    public class Notification : BaseEntity<int>
    {
        public Guid UserId { get; set; } = Guid.Empty;

        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public NotificationType Type { get; set; }

        public bool IsRead { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Loose reference — intentionally not a real FK so deleting an appointment
        // doesn't cascade-delete or block deletion of its notifications
        public int? RelatedEntityId { get; set; }

        // Navigation
        public ApplicationUser User { get; set; } = null!;
    }
}