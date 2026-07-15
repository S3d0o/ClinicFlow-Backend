namespace Shared.DTOs.Review
{
    public record SubmitReviewRequest(
    int AppointmentId,
    int Rating,        // 1-5
    string? Comment);      // max 300 chars
}
