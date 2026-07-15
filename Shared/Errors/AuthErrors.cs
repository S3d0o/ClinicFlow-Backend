using Shared.ResultPattern;

namespace Shared.Errors
{
    public static class AuthErrors
    {
        public static readonly Error InvalidCredentials =
       Error.InvalidCredentials("Auth.InvalidCredentials", "Email or password is incorrect");

        public static readonly Error AccountNotActive =
            Error.Unauthorized("Auth.AccountNotActive", "Your account has been deactivated");

        public static readonly Error EmailNotConfirmed =
            Error.Unauthorized("Auth.EmailNotConfirmed", "Please confirm your email before logging in");

        public static readonly Error EmailAlreadyExists =
            Error.Failure("Auth.EmailAlreadyExists", "An account with this email already exists");

        public static readonly Error InvalidRefreshToken =
            Error.Unauthorized("Auth.InvalidRefreshToken", "Refresh token is invalid or expired");

        public static readonly Error InvalidEmailConfirmationToken =
            Error.Failure("Auth.InvalidEmailConfirmationToken", "Email confirmation token is invalid or expired");

        public static readonly Error InvalidPasswordResetToken =
            Error.Failure("Auth.InvalidPasswordResetToken", "Password reset token is invalid or expired");

        public static readonly Error UserNotFound =
            Error.NotFound("Auth.UserNotFound", "No account found with this email");

        public static Error RegistrationFailed(IEnumerable<string> errors) =>
            Error.Failure("Auth.RegistrationFailed", string.Join(", ", errors));
    }
}
