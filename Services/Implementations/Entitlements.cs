namespace Services.Implementations
{
    // The current organization's effective modules and limits (docs/v2/adr/0008 decisions 6–7): read once through
    // IEntitlementQueries, computed with Modules.Effective and cached per organization in EntitlementCache. Core
    // modules are always on, so asking about one never reads the database.
    //
    // Not here yet: TryConsumeAsync for the metered quotas (UsageRecord) comes with the phone channels (P1 PR 9),
    // and OrganizationLimitOverride with the platform console; until then a limit is the plan's value
    public sealed class Entitlements(
        ITenantContext tenant, IEntitlementQueries queries, EntitlementCache cache, ILogger<Entitlements> logger) : IEntitlements
    {
        public async Task<bool> IsEnabledAsync(string module, CancellationToken ct)
        {
            // A typo must not quietly switch a feature off (or on)
            if (!Modules.All.Contains(module))
                throw new ArgumentException($"Unknown module '{module}'", nameof(module));

            if (Modules.IsCore(module))
                return true;

            return (await SnapshotAsync(ct)).Modules.Contains(module);
        }

        public async Task<int?> LimitAsync(string key, CancellationToken ct)
        {
            if (!Limits.All.Contains(key))
                throw new ArgumentException($"Unknown limit '{key}'", nameof(key));

            return (await SnapshotAsync(ct)).Limits.TryGetValue(key, out var value) ? value : null;
        }

        public async Task<IReadOnlySet<string>> EnabledModulesAsync(CancellationToken ct)
            => (await SnapshotAsync(ct)).Modules;

        private async Task<EntitlementSnapshot> SnapshotAsync(CancellationToken ct)
        {
            var organizationId = tenant.OrganizationId;
            if (cache.TryGet(organizationId, out var cached))
                return cached;

            var stamp = cache.StampOf(organizationId);
            var data = await queries.GetCurrentOrganizationAsync(ct);
            if (data.PlanCode is null)
                logger.LogWarning("Organization {OrganizationId} has no subscription; only the core modules are enabled", organizationId);

            var snapshot = new EntitlementSnapshot(
                Modules.Effective(data.PlanModules, data.Overrides.Select(o => (o.Module, o.Enabled))),
                data.PlanLimits);
            cache.TrySet(organizationId, stamp, snapshot);
            return snapshot;
        }
    }
}
