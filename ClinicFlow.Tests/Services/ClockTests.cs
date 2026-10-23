using Microsoft.Extensions.Options;
using Services.Implementations;
using Shared.Settings;

namespace ClinicFlow.Tests.Services
{
    public class ClockTests
    {
        private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => utcNow;
        }

        private static Clock CreateAt(DateTime utcNow, string timeZoneId = "Africa/Cairo")
            => new(new FixedTimeProvider(new DateTimeOffset(utcNow, TimeSpan.Zero)),
                Options.Create(new ClinicSettings { TimeZoneId = timeZoneId }));

        [Fact]
        public void UtcNow_IsTheProvidersTime_WithUtcKind()
        {
            var sut = CreateAt(new DateTime(2026, 1, 15, 10, 0, 0));

            Assert.Equal(new DateTime(2026, 1, 15, 10, 0, 0), sut.UtcNow);
            Assert.Equal(DateTimeKind.Utc, sut.UtcNow.Kind); // serialized with "Z"
        }

        [Fact]
        public void ClinicNow_ForCairoInWinter_IsUtcPlusTwo()
            => Assert.Equal(new DateTime(2026, 1, 15, 12, 0, 0), CreateAt(new DateTime(2026, 1, 15, 10, 0, 0)).ClinicNow);

        [Fact]
        public void ClinicNow_ForCairoInSummer_IsUtcPlusThree()
            // Egypt has observed daylight saving again since 2023 (REVIEW.md M2)
            => Assert.Equal(new DateTime(2026, 7, 15, 13, 0, 0), CreateAt(new DateTime(2026, 7, 15, 10, 0, 0)).ClinicNow);

        [Fact]
        public void ClinicNow_IsWallClockTime_WithoutUtcKind()
            => Assert.NotEqual(DateTimeKind.Utc, CreateAt(new DateTime(2026, 1, 15, 10, 0, 0)).ClinicNow.Kind);

        [Fact]
        public void ClinicToday_AfterMidnightInCairo_IsTheClinicsDateNotTheUtcOne()
            => Assert.Equal(new DateOnly(2026, 1, 16), CreateAt(new DateTime(2026, 1, 15, 22, 30, 0)).ClinicToday);

        [Fact]
        public void ClinicNow_ForUtc_EqualsUtcNow()
        {
            var sut = CreateAt(new DateTime(2026, 1, 15, 10, 0, 0), "Etc/UTC");

            Assert.Equal(sut.UtcNow, sut.ClinicNow);
        }

        [Fact]
        public void Constructor_WithUnknownZone_Throws()
            => Assert.ThrowsAny<TimeZoneNotFoundException>(() => CreateAt(DateTime.UnixEpoch, "Mars/Olympus"));

        // ── Branch zones (docs/v2/adr/0003) ──────────────────────────────────

        [Fact]
        public void LocalNow_IsTheTimeInTheGivenZone_NotTheClinicsZone()
        {
            var sut = CreateAt(new DateTime(2026, 1, 15, 10, 0, 0), "Etc/UTC");

            Assert.Equal(new DateTime(2026, 1, 15, 12, 0, 0), sut.LocalNow("Africa/Cairo"));
            Assert.Equal(new DateTime(2026, 1, 15, 11, 0, 0), sut.LocalNow("Europe/Berlin"));
        }

        [Fact]
        public void Today_AfterMidnightInTheZone_IsThatZonesDate()
            => Assert.Equal(new DateOnly(2026, 1, 16), CreateAt(new DateTime(2026, 1, 15, 22, 30, 0)).Today("Africa/Cairo"));

        [Fact]
        public void ToUtc_UsesTheZonesRules()
        {
            var sut = CreateAt(DateTime.UnixEpoch);

            Assert.Equal(new DateTime(2026, 7, 15, 7, 0, 0, DateTimeKind.Utc), sut.ToUtc("Africa/Cairo", new DateOnly(2026, 7, 15), new TimeOnly(10, 0)));
            Assert.Null(sut.ToUtc("Africa/Cairo", new DateOnly(2026, 4, 24), new TimeOnly(0, 30))); // skipped by daylight saving
        }

        [Fact]
        public void ToUtc_WithUnknownZone_Throws()
            // A branch with a bad zone must fail loudly, not fall back to another zone
            => Assert.ThrowsAny<TimeZoneNotFoundException>(
                () => CreateAt(DateTime.UnixEpoch).ToUtc("Mars/Olympus", new DateOnly(2026, 1, 1), new TimeOnly(9, 0)));
    }
}
