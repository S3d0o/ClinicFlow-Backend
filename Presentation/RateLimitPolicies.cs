namespace Presentation
{
    // Rate limiter policy names, registered in ClinicFlow/Extensions/WebApiExtensions.cs
    public static class RateLimitPolicies
    {
        // Login, registration, email confirmation and password reset (REVIEW.md M6)
        public const string Auth = "auth";
    }
}
