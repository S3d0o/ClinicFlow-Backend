namespace Shared.Settings
{
    public class ClinicSettings
    {
        // IANA time zone of the clinic. Slot dates and times are wall-clock times in this zone (REVIEW.md M2)
        public string TimeZoneId { get; init; } = "Africa/Cairo";
    }
}
