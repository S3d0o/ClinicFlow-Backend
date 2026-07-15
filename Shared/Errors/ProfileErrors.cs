using Shared.ResultPattern;

namespace Shared.Errors
{
    public static class ProfileErrors
    {
        public static readonly Error NotFound =
            Error.NotFound("Profile.NotFound", "User profile not found");

        public static readonly Error UpdateFailed =
            Error.Failure("Profile.UpdateFailed", "Failed to update profile");
    }
}
