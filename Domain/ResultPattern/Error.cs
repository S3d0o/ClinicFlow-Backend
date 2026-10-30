namespace Domain.ResultPattern
{
    public sealed record Error(
    string Code,
    string Description,
    ErrorType Type)
    {
        private static readonly IReadOnlyDictionary<string, object> NoMetadata = new Dictionary<string, object>();

        // Members ProblemDetails already has, which metadata must not overwrite
        private static readonly HashSet<string> ReservedKeys =
            new(StringComparer.OrdinalIgnoreCase) { "type", "title", "status", "detail", "instance", "traceId", "errors" };

        // Extra members of the error, rendered at the top level of its ProblemDetails (contract §13.1 "Error extensions"),
        // such as retryAt and attemptsLeft. Keys are the JSON names (camelCase). Add them with WithMetadata
        public IReadOnlyDictionary<string, object> Metadata { get; private init; } = NoMetadata;

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

        public static Error TooManyRequests(
            string code = "Request.TooMany",
            string description = "Too many requests")
            => new(code, description, ErrorType.TooManyRequests);

        // A copy of this error with one more metadata member; the original (often a static readonly error) is unchanged
        public Error WithMetadata(string key, object value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentNullException.ThrowIfNull(value);
            if (ReservedKeys.Contains(key))
                throw new ArgumentException($"'{key}' is a ProblemDetails member and can't be error metadata.", nameof(key));

            return this with { Metadata = new Dictionary<string, object>(Metadata) { [key] = value } };
        }

        // When the caller may try again: lockouts, cooldowns and limits. An instant, so it must be UTC (sent with a Z)
        public Error WithRetryAt(DateTime retryAtUtc)
        {
            if (retryAtUtc.Kind != DateTimeKind.Utc)
                throw new ArgumentException("retryAt must be a UTC instant (DateTimeKind.Utc).", nameof(retryAtUtc));

            return WithMetadata(ErrorMetadata.RetryAt, retryAtUtc);
        }

        // Tries left before a lockout, e.g. after a wrong one-time code
        public Error WithAttemptsLeft(int attemptsLeft)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(attemptsLeft);
            return WithMetadata(ErrorMetadata.AttemptsLeft, attemptsLeft);
        }

        // Errors are compared by value, metadata included, so a test can compare an error built twice
        public bool Equals(Error? other)
            => other is not null
               && Code == other.Code
               && Description == other.Description
               && Type == other.Type
               && Metadata.Count == other.Metadata.Count
               && Metadata.All(m => other.Metadata.TryGetValue(m.Key, out var value) && Equals(m.Value, value));

        public override int GetHashCode() => HashCode.Combine(Code, Description, Type, Metadata.Count);
    }

    // The metadata keys the contract defines (API_CONTRACT.md §13.1, §13.10)
    public static class ErrorMetadata
    {
        public const string RetryAt = "retryAt";
        public const string AttemptsLeft = "attemptsLeft";
    }
}
