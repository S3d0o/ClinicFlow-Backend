namespace Domain.ResultPattern
{
    // Each type maps to one status code in ApiController: Failure 500, Validation 400, NotFound 404, Unauthorized 401,
    // Forbidden 403, InvalidCredentials 401 (400 on /api/v2), Conflict 409, TooManyRequests 429
    public enum ErrorType
    {
        Failure,
        Validation,
        NotFound,
        // Only for token problems on /api/v2 (contract §13.1): a missing, invalid or expired token, or a bad refresh token.
        // A wrong password or code is Validation or InvalidCredentials (400), a lockout TooManyRequests, a blocked account Forbidden
        Unauthorized,
        Forbidden,
        InvalidCredentials,
        Conflict,
        // Lockouts, cooldowns and limits (429); give it a retryAt with Error.WithRetryAt (contract §13.1, §13.10)
        TooManyRequests
    }
}
