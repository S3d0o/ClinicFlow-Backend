using Shared.Enums;

namespace Shared.DTOs.Profile
{
    public record UserProfileResponse(
     Guid Id,
     string FirstName,
     string LastName,
     string Email,
     string? PhoneNumber,
     string? ProfilePictureUrl,
     Gender Gender,
     DateOnly? DateOfBirth,
     string Role,
     DateTime CreatedAt);
}
