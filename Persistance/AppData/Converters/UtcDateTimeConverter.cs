using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Persistence.AppData.Converters
{
    // Every DateTime column holds UTC (they come from IClock.UtcNow), but SQL Server's datetime2 keeps no kind, so EF reads
    // them back as Unspecified and the JSON lacks the "Z". Marking them Utc on read makes the API send "...Z" (REVIEW.md §4.1).
    // Clinic-local slot times are DateOnly/TimeOnly columns and aren't affected.
    public class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        value => value,
        value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
