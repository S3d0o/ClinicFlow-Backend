namespace Shared.DTOs.Specialty
{
    public record SpecialtyRequest(
        string Name,
        string? Description,
        string? IconUrl);
    
}
