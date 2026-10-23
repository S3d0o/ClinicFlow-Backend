namespace Domain.Time
{
    // Converts a branch's wall-clock date and time to the UTC instant that business rules compare with (docs/v2/adr/0003).
    // Daylight saving (Egypt observes it again since 2023):
    // - a local time skipped when the clocks go forward doesn't exist, so it has no instant (null) and no slot is made
    // - a local time repeated when the clocks go back means its first occurrence, still under daylight saving.
    //   TimeZoneInfo.GetUtcOffset would pick the second one (standard time)
    public static class WallClock
    {
        public static DateTime? ToUtc(TimeZoneInfo zone, DateOnly date, TimeOnly time)
        {
            var local = date.ToDateTime(time, DateTimeKind.Unspecified);
            if (zone.IsInvalidTime(local))
                return null;

            var offset = zone.IsAmbiguousTime(local)
                ? zone.GetAmbiguousTimeOffsets(local).Max()
                : zone.GetUtcOffset(local);

            return DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);
        }
    }
}
