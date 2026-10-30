using Shared.DTOs.Entitlements;

namespace Services.Abstraction.Contracts
{
    // The subscription rows behind IEntitlements, read untracked for the current organization (CQRS-lite, implemented
    // in Persistence). Only IEntitlements calls it: everything else asks IEntitlements
    public interface IEntitlementQueries
    {
        Task<OrganizationEntitlementsData> GetCurrentOrganizationAsync(CancellationToken ct);
    }
}
