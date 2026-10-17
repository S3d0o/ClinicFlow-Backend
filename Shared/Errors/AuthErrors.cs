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
            Error.Conflict("Auth.EmailAlreadyExists", "An account with this email already exists");

        public static readonly Error InvalidRefreshToken =
            Error.Unauthorized("Auth.InvalidRefreshToken", "Refresh token is invalid or expired");

        public static readonly Error InvalidEmailConfirmationToken =
            Error.Validation("Auth.InvalidEmailConfirmationToken", "Email confirmation token is invalid or expired");

        public static readonly Error InvalidPasswordResetToken =
            Error.Validation("Auth.InvalidPasswordResetToken", "Password reset token is invalid or expired");

        public static readonly Error UserNotFound =
            Error.NotFound("Auth.UserNotFound", "User account was not found");

        public static readonly Error AccountLockedOut =
            Error.Unauthorized("Auth.AccountLockedOut", "Too many failed login attempts. Please try again later");

        // Identity rejected the user (e.g. the password policy): the caller can fix the request
        public static Error RegistrationFailed(IEnumerable<string> errors) =>
            Error.Validation("Auth.RegistrationFailed", string.Join(", ", errors));

        // An unexpected server-side failure while registering; same code, but a real 500
        public static readonly Error RegistrationError =
            Error.Failure("Auth.RegistrationFailed", "An error occurred during registration. Please try again later.");
    }
}
