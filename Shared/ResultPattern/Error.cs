namespace Shared.ResultPattern
{
    public sealed record Error(
    string Code,
    string Description,
    ErrorType Type)
    {
        // Sentinel — use when you need an "empty" error slot
        public static readonly Error None
            = new(string.Empty, string.Empty, ErrorType.Failure);

        public static Error Failure(
            string code = "General.Failure",
            string description = "A general failure occurred")
            => new(code, description, ErrorType.Failure);

        public static Error Validation(
            string code = "Validation.Failure",
            string description = "Validation error occurred")
            => new(code, description, ErrorType.Validation);

        public static Error NotFound(
            string code = "Resource.NotFound",
            string description = "Requested resource was not found")
            => new(code, description, ErrorType.NotFound);

        public static Error Unauthorized(
            string code = "Auth.Unauthorized",
            string description = "Unauthorized access")
            => new(code, description, ErrorType.Unauthorized);

        public static Error Forbidden(
            string code = "Auth.Forbidden",
            string description = "Forbidden access")
            => new(code, description, ErrorType.Forbidden);

        public static Error InvalidCredentials(
            string code = "Auth.InvalidCredentials",
            string description = "Invalid credentials provided")
            => new(code, description, ErrorType.InvalidCredentials);
        public static Error Conflict(
            string code = "Resource.Conflict",
            string description = "Resource conflict occurred")
            => new(code, description, ErrorType.Conflict);
    }
}
