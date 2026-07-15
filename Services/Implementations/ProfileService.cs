using Domain.Entities.IdentityModule;
using Microsoft.AspNetCore.Identity;
using Shared.DTOs.Profile;

namespace Services.Implementations
{
    public class ProfileService(
     UserManager<ApplicationUser> userManager,
     ILogger<ProfileService> logger) : IProfileService
    {
        public async Task<Result<UserProfileResponse>> GetProfileAsync(Guid userId, CancellationToken ct)
        {
            var user = await userManager.FindByIdAsync(userId.ToString());
            if (user is null)
            {
                logger.LogWarning("Profile not found for UserId {UserId}", userId);
                return ProfileErrors.NotFound;
            }

            var roles = await userManager.GetRolesAsync(user);
            var response = new UserProfileResponse(
                     user.Id,
                     user.FirstName,
                     user.LastName,
                     user.Email!,
                     user.PhoneNumber,
                     user.ProfilePictureUrl,
                     user.Gender,
                     user.DateOfBirth,
                     roles.FirstOrDefault() ?? string.Empty,
                     user.CreatedAt);

            logger.LogInformation("Profile retrieved for UserId {UserId}", userId);
            return response;
        }

        public async Task<Result> UpdateProfileAsync(
            Guid userId, UpdateUserProfileRequest request, CancellationToken ct)
        {
            var user = await userManager.FindByIdAsync(userId.ToString());
            if (user is null)
            {
                logger.LogWarning("Profile not found for UserId {UserId}", userId);
                return ProfileErrors.NotFound;
            }

            // Only apply non-null fields — PATCH semantics
            if (request.FirstName is not null) user.FirstName = request.FirstName;
            if (request.LastName is not null) user.LastName = request.LastName;
            if (request.PhoneNumber is not null) user.PhoneNumber = request.PhoneNumber;
            if (request.ProfilePictureUrl is not null) user.ProfilePictureUrl = request.ProfilePictureUrl;
            if (request.Gender is not null) user.Gender = request.Gender.Value;
            if (request.DateOfBirth is not null) user.DateOfBirth = request.DateOfBirth;

            // Use UserManager.UpdateAsync — not EF directly
            // Ensures Identity internal state stays consistent
            var result = await userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                logger.LogWarning("Profile update failed for UserId {UserId}: {Errors}",
                    userId, string.Join(", ", result.Errors.Select(e => e.Description)));
                return ProfileErrors.UpdateFailed;
            }

            logger.LogInformation("Profile updated for UserId {UserId}", userId);
            return Result.Ok();
        }
    }
}
