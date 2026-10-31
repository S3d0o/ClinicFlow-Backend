using Shared.DTOs.Organization;
using System.Collections.Concurrent;

namespace Services.Implementations
{
    // The settings per organization, in memory for the life of the process; a singleton. OrganizationSettingsService
    // invalidates an organization once its change has committed. An entry also expires after Lifetime, for changes
    // this process can't see (SQL run by hand, another instance of the app).
    //
    // As in EntitlementCache, a load reads the organization's version first and stores its result only if no
    // invalidation happened meanwhile, so a load that read the rows just before a change can't put the old values back
    public sealed class OrganizationSettingsCache(IClock clock)
    {
        public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

        private readonly ConcurrentDictionary<int, (OrganizationSettingsResponse Settings, DateTime ExpiresAt)> _entries = new();
        private readonly ConcurrentDictionary<int, long> _versions = new();

        public bool TryGet(int organizationId, out OrganizationSettingsResponse settings)
        {
            if (_entries.TryGetValue(organizationId, out var entry) && entry.ExpiresAt > clock.UtcNow)
            {
                settings = entry.Settings;
                return true;
            }

            settings = null!;
            return false;
        }

        public long VersionOf(int organizationId) => _versions.GetValueOrDefault(organizationId);

        public void TrySet(int organizationId, long version, OrganizationSettingsResponse settings)
        {
            lock (_entries)
            {
                if (VersionOf(organizationId) == version)
                    _entries[organizationId] = (settings, clock.UtcNow + Lifetime);
            }
        }

        public void Invalidate(int organizationId)
        {
            lock (_entries)
            {
                _versions.AddOrUpdate(organizationId, 1, (_, version) => version + 1);
                _entries.TryRemove(organizationId, out _);
            }
        }

        // Every organization, e.g. after the database was replaced underneath the process (integration tests)
        public void InvalidateAll()
        {
            lock (_entries)
            {
                foreach (var organizationId in _entries.Keys.Union(_versions.Keys).ToList())
                    _versions.AddOrUpdate(organizationId, 1, (_, version) => version + 1);
                _entries.Clear();
            }
        }
    }
}
