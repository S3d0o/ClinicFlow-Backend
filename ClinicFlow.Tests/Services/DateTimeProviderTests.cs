using Microsoft.Extensions.Options;
using Services.Implementations;
using Shared.Settings;

namespace ClinicFlow.Tests.Services
{
    public class DateTimeProviderTests
    {
        [Fact]
        public void ClinicNow_ForCairo_IsUtcConvertedToTheClinicZone()
        {
            var sut = new DateTimeProvider(Options.Create(new ClinicSettings { TimeZoneId = "Africa/Cairo" }));
            var cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");

            var expected = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, cairo);
            var clinicNow = sut.ClinicNow;

            // Cairo is UTC+2, or UTC+3 under daylight saving (REVIEW.md M2)
            Assert.InRange(clinicNow - expected, TimeSpan.Zero, TimeSpan.FromSeconds(5));
            Assert.InRange((clinicNow - DateTime.UtcNow).TotalHours, 1.9, 3.1);
        }

        [Fact]
        public void ClinicNow_ForUtc_EqualsUtcNow()
        {
            var sut = new DateTimeProvider(Options.Create(new ClinicSettings { TimeZoneId = "Etc/UTC" }));

            Assert.InRange(sut.ClinicNow - DateTime.UtcNow, TimeSpan.FromSeconds(-5), TimeSpan.FromSeconds(5));
        }

        [Fact]
        public void Constructor_WithUnknownZone_Throws()
            => Assert.ThrowsAny<TimeZoneNotFoundException>(
                () => new DateTimeProvider(Options.Create(new ClinicSettings { TimeZoneId = "Mars/Olympus" })));
    }
}
