using Domain.Entities.Subscriptions;

namespace Persistence.Repositories
{
    // The tenancy filters keep the subscription and overrides to the context's organization
    public class SubscriptionRepo(ClinicDbContext context) : ISubscriptionRepo
    {
        public Task<OrganizationSubscription?> GetCurrentAsync(CancellationToken ct)
            => context.OrganizationSubscriptions
                .Include(s => s.Plan).ThenInclude(p => p.Modules)
                .FirstOrDefaultAsync(ct);

        public Task<Plan?> GetPlanByCodeAsync(string planCode, CancellationToken ct)
            => context.Plans
                .Include(p => p.Modules)
                .FirstOrDefaultAsync(p => p.Code == planCode, ct);

        public Task<List<OrganizationModuleOverride>> GetOverridesAsync(CancellationToken ct)
            => context.OrganizationModuleOverrides.ToListAsync(ct);

        // The organization is part of the key, so it is set here rather than by the tenant interceptor: the audit entry,
        // written before that interceptor runs, names the row by its key
        public void AddOverride(OrganizationModuleOverride moduleOverride)
        {
            moduleOverride.OrganizationId = context.OrganizationId;
            context.OrganizationModuleOverrides.Add(moduleOverride);
        }

        public void RemoveOverride(OrganizationModuleOverride moduleOverride)
            => context.OrganizationModuleOverrides.Remove(moduleOverride);
    }
}
