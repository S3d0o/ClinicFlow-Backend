namespace Services.Abstraction.Contracts
{
    // The only source of "now" (REVIEW.md §4.1): nothing else reads DateTime.UtcNow / Now, and a test enforces it.
    // Branch-aware members (a time zone per branch, ADR 0003) come with P0 PR 6.
    public interface IClock
    {
        // Kind is Utc. For stored timestamps (BookedAt, CancelledAt, CreatedAt, token expiry, ...)
        DateTime UtcNow { get; }

        // Wall-clock time at the clinic (Clinic:TimeZoneId). Slot dates and times are clinic-local,
        // so every rule that compares a slot with "now" or "today" uses this (REVIEW.md M2)
        DateTime ClinicNow { get; }

        // The clinic's calendar date, e.g. for slot generation and age checks
        DateOnly ClinicToday { get; }
    }
}
