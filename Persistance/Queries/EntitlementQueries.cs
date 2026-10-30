using Services.Abstraction.Contracts;
using Shared.DTOs.Entitlements;

namespace Persistence.Queries
{
    // The subscription rows behind IEntitlements (docs/v2/adr/0008 decision 7), untracked. The tenancy filters keep
    // both reads to the context's organization; plans are platform data and are reached through the subscription
    public class EntitlementQueries(ClinicDbContext context) : IEntitlementQueries
    {
        public async Task<OrganizationEntitlementsData> GetCurrentOrganizationAsync(CancellationToken ct)
        {
            var subscription = await context.OrganizationSubscriptions
                .AsNoTracking()
                .Select(s => new
                {
                    s.Plan.Code,
                    Modules = s.Plan.Modules.Select(m => m.ModuleCode).ToList(),
                    Limits = s.Plan.Limits.Select(l => new { l.LimitKey, l.Value }).ToList(),
                })
                .FirstOrDefaultAsync(ct);

            var overrides = await context.OrganizationModuleOverrides
                .AsNoTracking()
                .OrderBy(o => o.ModuleCode)
                .Select(o => new ModuleOverrideData(o.ModuleCode, o.Enabled))
                .ToListAsync(ct);

            return subscription is null
                ? OrganizationEntitlementsData.None with { Overrides = overrides }
                : new OrganizationEntitlementsData(
                    subscription.Code,
                    subscription.Modules,
                    subscription.Limits.ToDictionary(l => l.LimitKey, l => l.Value),
                    overrides);
        }
    }
}
