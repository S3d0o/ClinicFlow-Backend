using System.Collections.Concurrent;

namespace Services.Implementations
{
    // An organization's effective modules and limits, as Entitlements computed them
    public sealed record EntitlementSnapshot(IReadOnlySet<string> Modules, IReadOnlyDictionary<string, int> Limits);

    // Effective entitlements per organization, in memory for the life of the process (docs/v2/adr/0008 decision 7).
    // A singleton. Persistence invalidates an organization after a save that changes its subscription or overrides,
    // and every organization after one that changes a plan (EntitlementSaveChangesInterceptor). An entry also expires
    // after Lifetime, for changes this process can't see: SQL run by hand, or another instance of the app.
    //
    // A load reads the stamp first and stores its result only if no invalidation happened meanwhile, so a load that
    // read the rows just before a change can't put the old entitlements back after the change dropped them
    public sealed class EntitlementCache(IClock clock) : IEntitlementCache
    {
        public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

        private readonly ConcurrentDictionary<int, (EntitlementSnapshot Snapshot, DateTime ExpiresAt)> _entries = new();
        private readonly Dictionary<int, long> _versions = [];
        private readonly Lock _gate = new();
        private long _generation; // bumped by InvalidateAll

        // Taken before a load; TrySet compares it
        public readonly record struct Stamp(long Generation, long Version);

        public bool TryGet(int organizationId, out EntitlementSnapshot snapshot)
        {
            if (_entries.TryGetValue(organizationId, out var entry) && entry.ExpiresAt > clock.UtcNow)
            {
                snapshot = entry.Snapshot;
                return true;
            }

            snapshot = null!;
            return false;
        }

        public Stamp StampOf(int organizationId)
        {
            lock (_gate)
                return new Stamp(_generation, _versions.GetValueOrDefault(organizationId));
        }

        // False (and nothing stored) when the organization was invalidated after the stamp was taken
        public bool TrySet(int organizationId, Stamp stamp, EntitlementSnapshot snapshot)
        {
            lock (_gate)
            {
                if (stamp != new Stamp(_generation, _versions.GetValueOrDefault(organizationId)))
                    return false;
                _entries[organizationId] = (snapshot, clock.UtcNow + Lifetime);
                return true;
            }
        }

        public void Invalidate(int organizationId)
        {
            lock (_gate)
            {
                _versions[organizationId] = _versions.GetValueOrDefault(organizationId) + 1;
                _entries.TryRemove(organizationId, out _);
            }
        }

        public void InvalidateAll()
        {
            lock (_gate)
            {
                _generation++;
                _entries.Clear();
            }
        }
    }
}
