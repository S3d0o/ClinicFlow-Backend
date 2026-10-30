using NSubstitute;
using Services.Abstraction.Contracts;
using Services.Implementations;

namespace ClinicFlow.Tests.Services.EntitlementsTests
{
    public class EntitlementCacheTests
    {
        private static readonly DateTime UtcNow = new(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc);
        private static readonly EntitlementSnapshot Snapshot = new(new HashSet<string> { "FD" }, new Dictionary<string, int>());

        private readonly IClock _clock = Substitute.For<IClock>();
        private readonly EntitlementCache _sut;

        public EntitlementCacheTests()
        {
            _clock.UtcNow.Returns(UtcNow);
            _sut = new EntitlementCache(_clock);
        }

        [Fact]
        public void TrySet_ThenTryGet_ReturnsTheSnapshot()
        {
            Assert.True(_sut.TrySet(1, _sut.StampOf(1), Snapshot));

            Assert.True(_sut.TryGet(1, out var cached));
            Assert.Same(Snapshot, cached);
            Assert.False(_sut.TryGet(2, out _));
        }

        [Fact]
        public void Invalidate_DropsOnlyThatOrganization()
        {
            _sut.TrySet(1, _sut.StampOf(1), Snapshot);
            _sut.TrySet(2, _sut.StampOf(2), Snapshot);

            _sut.Invalidate(1);

            Assert.False(_sut.TryGet(1, out _));
            Assert.True(_sut.TryGet(2, out _));
        }

        [Fact]
        public void TrySet_AfterAnInvalidationSinceTheStamp_StoresNothing()
        {
            // A load that read the rows before a change must not put the old entitlements back after it
            var stamp = _sut.StampOf(1);
            _sut.Invalidate(1);

            Assert.False(_sut.TrySet(1, stamp, Snapshot));
            Assert.False(_sut.TryGet(1, out _));
        }

        [Fact]
        public void TrySet_AfterInvalidateAllSinceTheStamp_StoresNothing()
        {
            var stamp = _sut.StampOf(1);
            _sut.InvalidateAll();

            Assert.False(_sut.TrySet(1, stamp, Snapshot));
        }

        [Fact]
        public void InvalidateAll_DropsEveryOrganization()
        {
            _sut.TrySet(1, _sut.StampOf(1), Snapshot);
            _sut.TrySet(2, _sut.StampOf(2), Snapshot);

            _sut.InvalidateAll();

            Assert.False(_sut.TryGet(1, out _));
            Assert.False(_sut.TryGet(2, out _));
        }

        [Fact]
        public void AnInvalidationOfAnotherOrganization_DoesNotStaleTheStamp()
        {
            var stamp = _sut.StampOf(1);
            _sut.Invalidate(2);

            Assert.True(_sut.TrySet(1, stamp, Snapshot));
        }

        [Fact]
        public void TryGet_AfterTheLifetime_Misses()
        {
            // Changes this process can't see (SQL by hand, another instance) show within Lifetime
            _sut.TrySet(1, _sut.StampOf(1), Snapshot);

            _clock.UtcNow.Returns(UtcNow + EntitlementCache.Lifetime - TimeSpan.FromSeconds(1));
            Assert.True(_sut.TryGet(1, out _));

            _clock.UtcNow.Returns(UtcNow + EntitlementCache.Lifetime);
            Assert.False(_sut.TryGet(1, out _));
        }
    }
}
