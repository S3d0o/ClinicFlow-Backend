using Domain.Entities.Subscriptions;

namespace Domain.Interfaces.IRepositories
{
    // The current organization's subscription and module overrides, tracked, for the platform switches
    // (docs/v2/adr/0008). Reads for authorization go through IEntitlements, never through here
    public interface ISubscriptionRepo
    {
        // With its plan and the plan's modules; null when the organization has none
        Task<OrganizationSubscription?> GetCurrentAsync(CancellationToken ct);

        // With its modules; plans are platform data, so this sees every plan
        Task<Plan?> GetPlanByCodeAsync(string planCode, CancellationToken ct);

        Task<List<OrganizationModuleOverride>> GetOverridesAsync(CancellationToken ct);

        void AddOverride(OrganizationModuleOverride moduleOverride);

        void RemoveOverride(OrganizationModuleOverride moduleOverride);
    }
}
