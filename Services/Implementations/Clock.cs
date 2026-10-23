using System.Collections.Concurrent;
using Domain.Time;
using Microsoft.Extensions.Options;
using Shared.Settings;

namespace Services.Implementations
{
    // TimeProvider is .NET's system clock (TimeProvider.System in production), so tests can freeze time
    public class Clock(TimeProvider timeProvider, IOptions<ClinicSettings> options) : IClock
    {
        private readonly TimeZoneInfo _clinicZone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZoneId);
        private readonly ConcurrentDictionary<string, TimeZoneInfo> _zones = new();

        public DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;

        public DateTime ClinicNow => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, _clinicZone);

        public DateOnly ClinicToday => DateOnly.FromDateTime(ClinicNow);

        public DateTime LocalNow(string timeZoneId) => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, Zone(timeZoneId));

        public DateOnly Today(string timeZoneId) => DateOnly.FromDateTime(LocalNow(timeZoneId));

        public DateTime? ToUtc(string timeZoneId, DateOnly date, TimeOnly time) => WallClock.ToUtc(Zone(timeZoneId), date, time);

        // An unknown id throws TimeZoneNotFoundException: a branch with a bad zone must not silently use another one
        private TimeZoneInfo Zone(string timeZoneId) => _zones.GetOrAdd(timeZoneId, TimeZoneInfo.FindSystemTimeZoneById);
    }
}
