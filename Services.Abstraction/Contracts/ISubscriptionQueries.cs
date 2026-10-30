using Shared.DTOs.Entitlements;

namespace Services.Abstraction.Contracts
{
    // The entitlement reads behind the "Plan & modules" screen and the frontend's module gating (docs/v2/adr/0008
    // decision 12; CQRS-lite, implemented in Persistence). Modules and limits come from IEntitlements, so they are the
    // ones authorization enforces
    public interface ISubscriptionQueries
    {
        // The current organization's plan, status, effective modules, overrides and limits with their usage
        Task<Result<EntitlementsResponse>> GetEntitlementsAsync(CancellationToken ct);

        // The current organization's enabled modules, core included, in catalog order
        Task<Result<IReadOnlyList<string>>> GetEnabledModulesAsync(CancellationToken ct);

        // The plans on the platform's price list, in their seeded order
        Task<Result<IReadOnlyList<PlanResponse>>> ListPlansAsync(CancellationToken ct);
    }
}
