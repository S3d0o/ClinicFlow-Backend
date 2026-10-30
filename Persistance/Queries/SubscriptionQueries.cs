using Microsoft.Extensions.Logging;
using Services.Abstraction.Contracts;
using Shared.DTOs.Entitlements;
using Shared.Errors;

namespace Persistence.Queries
{
    // Entitlement reads (CQRS-lite), untracked. The modules and limits are IEntitlements', the ones authorization
    // enforces; the subscription row, overrides and usage counts are read here. The tenancy filters keep every read to
    // the context's organization; plans are platform data
    public class SubscriptionQueries(ClinicDbContext context, IEntitlements entitlements, ILogger<SubscriptionQueries> logger)
        : ISubscriptionQueries
    {
        public async Task<Result<EntitlementsResponse>> GetEntitlementsAsync(CancellationToken ct)
        {
            var subscription = await context.OrganizationSubscriptions
                .AsNoTracking()
                .Select(s => new { PlanCode = s.Plan.Code, s.Status, s.TrialEndsAt, s.CurrentPeriodEnd })
                .FirstOrDefaultAsync(ct);
            if (subscription is null)
            {
                logger.LogWarning("Organization {OrganizationId} has no subscription", context.OrganizationId);
                return SubscriptionErrors.NotFound;
            }

            var overrides = await context.OrganizationModuleOverrides
                .AsNoTracking()
                .Select(o => new ModuleOverrideResponse(o.ModuleCode, o.Enabled))
                .ToListAsync(ct);

            var modules = Modules.InCatalogOrder(await entitlements.EnabledModulesAsync(ct));

            // What the counted limits count (decision 5): active branches and active, approved doctors. Phone messages
            // are metered once the phone channels and UsageRecord arrive (P1 PR 9); until then nothing is sent
            var used = new Dictionary<string, int>
            {
                [Limits.Branches] = await context.Branches.CountAsync(b => b.IsActive, ct),
                [Limits.Doctors] = await context.DoctorProfiles.CountAsync(d => d.IsApprovedByAdmin && d.User.IsActive, ct),
                [Limits.PhoneMessagesPerMonth] = 0,
            };

            var usage = new List<LimitUsageResponse>(Limits.Catalog.Count);
            foreach (var key in Limits.Catalog)
                usage.Add(new LimitUsageResponse(key, await entitlements.LimitAsync(key, ct), used[key]));

            return new EntitlementsResponse(
                subscription.PlanCode,
                subscription.Status,
                subscription.TrialEndsAt,
                subscription.CurrentPeriodEnd,
                modules,
                overrides.OrderBy(o => Modules.OrderOf(o.Module)).ToList(),
                usage);
        }

        public async Task<Result<IReadOnlyList<string>>> GetEnabledModulesAsync(CancellationToken ct)
            => Result<IReadOnlyList<string>>.Ok(Modules.InCatalogOrder(await entitlements.EnabledModulesAsync(ct)));

        public async Task<Result<IReadOnlyList<PlanResponse>>> ListPlansAsync(CancellationToken ct)
        {
            var plans = await context.Plans
                .AsNoTracking()
                .Where(p => p.IsPublic)
                .OrderBy(p => p.Id)
                .Select(p => new
                {
                    p.Code,
                    p.Name,
                    Modules = p.Modules.Select(m => m.ModuleCode).ToList(),
                    Limits = p.Limits.Select(l => new { l.LimitKey, l.Value }).ToList(),
                })
                .ToListAsync(ct);

            return Result<IReadOnlyList<PlanResponse>>.Ok(plans
                .Select(p =>
                {
                    var limits = p.Limits.ToDictionary(l => l.LimitKey, l => (int?)l.Value);
                    return new PlanResponse(
                        p.Code,
                        p.Name,
                        Modules.InCatalogOrder(p.Modules),
                        new PlanLimitsResponse(
                            limits.GetValueOrDefault(Limits.Branches),
                            limits.GetValueOrDefault(Limits.Doctors),
                            limits.GetValueOrDefault(Limits.PhoneMessagesPerMonth)));
                })
                .ToList());
        }
    }
}
