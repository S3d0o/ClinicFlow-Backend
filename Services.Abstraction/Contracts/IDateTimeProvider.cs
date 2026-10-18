namespace Services.Abstraction.Contracts
{
    public interface IDateTimeProvider
    {
        DateTime UtcNow { get; }

        // Wall-clock time at the clinic (Clinic:TimeZoneId). Slot dates and times are clinic-local,
        // so every rule that compares a slot with "now" or "today" uses this (REVIEW.md M2)
        DateTime ClinicNow { get; }
    }
}
