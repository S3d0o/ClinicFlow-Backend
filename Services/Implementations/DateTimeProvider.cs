using Microsoft.Extensions.Options;
using Shared.Settings;

namespace Services.Implementations
{
    public class DateTimeProvider(IOptions<ClinicSettings> options) : IDateTimeProvider
    {
        private readonly TimeZoneInfo _clinicZone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZoneId);

        public DateTime UtcNow => DateTime.UtcNow;

        public DateTime ClinicNow => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _clinicZone);
    }
}
