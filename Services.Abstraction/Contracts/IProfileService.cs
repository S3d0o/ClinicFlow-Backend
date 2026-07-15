using Shared.DTOs.Profile;

namespace Services.Abstraction.Contracts
{
    public interface IProfileService
    {
        Task<Result<UserProfileResponse>> GetProfileAsync(Guid userId, CancellationToken ct);
        Task<Result> UpdateProfileAsync(Guid userId, UpdateUserProfileRequest request, CancellationToken ct);
    }
}
