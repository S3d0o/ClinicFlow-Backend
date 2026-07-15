using Shared.Enums;

namespace Shared.DTOs.Profile
{
    public record UpdateUserProfileRequest
    {
        public string? FirstName { get; init; }
        public string? LastName { get; init; }
        public string? PhoneNumber { get; init; }
        public string? ProfilePictureUrl { get; init; }
        public Gender? Gender { get; init; }
        public DateOnly? DateOfBirth { get; init; }
    }
}
