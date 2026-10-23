namespace Services.Abstraction.Contracts
{
    // The only source of "now" (REVIEW.md §4.1): nothing else reads DateTime.UtcNow / Now, and a test enforces it.
    // Branch-local questions take the branch's IANA zone (Branch.TimeZoneId, from uow.Branches), so the clock stays a
    // stateless singleton (docs/v2/adr/0003)
    public interface IClock
    {
        // Kind is Utc. For stored timestamps (BookedAt, CancelledAt, CreatedAt, token expiry, ...) and for every rule
        // that compares a slot with "now", through AppointmentSlot.StartsAtUtc
        DateTime UtcNow { get; }

        // Wall-clock time in the configured clinic zone (Clinic:TimeZoneId), which DataSeeder keeps equal to the MAIN
        // branch's zone. Being replaced by the branch-aware members below (REVIEW.md M2)
        DateTime ClinicNow { get; }

        // The clinic's calendar date, e.g. for age checks
        DateOnly ClinicToday { get; }

        // Wall-clock time and calendar date in a branch's zone
        DateTime LocalNow(string timeZoneId);
        DateOnly Today(string timeZoneId);

        // The UTC instant of a branch-local date and time; null when daylight saving skips that time (Domain.Time.WallClock)
        DateTime? ToUtc(string timeZoneId, DateOnly date, TimeOnly time);
    }
}
