namespace Shared.DTOs.Review
{
    public record ReviewResponse
    {
        public int Id { get; init; }
        public int Rating { get; init; }          // 1–5, validated in service layer too
        public string? Comment { get; init; }     // max 300 chars
        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
        public bool IsVisible { get; init; } = true; // admin can hide without deleting
        public string PatientName { get; set; } = string.Empty;
    }
}
