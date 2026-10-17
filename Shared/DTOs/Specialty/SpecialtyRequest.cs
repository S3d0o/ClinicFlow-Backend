namespace Shared.DTOs.Specialty
{
    public record SpecialtyRequest(
        string Name,
        string? Description,
        string? IconUrl,
        // Omitted (null) keeps the current state; a new specialty is active unless false is sent (REVIEW.md L6)
        bool? IsActive = null);
}
