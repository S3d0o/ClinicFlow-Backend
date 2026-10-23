using Microsoft.Extensions.Options;
using Shared.Settings;

namespace Services.Implementations
{
    // TimeProvider is .NET's system clock (TimeProvider.System in production), so tests can freeze time
    public class Clock(TimeProvider timeProvider, IOptions<ClinicSettings> options) : IClock
    {
        private readonly TimeZoneInfo _clinicZone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZoneId);

        public DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;

        public DateTime ClinicNow => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, _clinicZone);

        public DateOnly ClinicToday => DateOnly.FromDateTime(ClinicNow);
    }
}
