namespace Shared.Settings
{
    // Per-client-IP fixed window for the credential and email endpoints (REVIEW.md M6).
    // Bound from RateLimiting:Auth; account lockout still applies on top of it.
    public class RateLimitSettings
    {
        public int PermitLimit { get; set; } = 10;
        public int WindowSeconds { get; set; } = 60;
    }
}
