using Shared.DTOs.Organization;

namespace Services.Abstraction.Contracts
{
    // Changes to the organization's profile (FR-ORG-01; API_CONTRACT.md §13.24). Reads are IOrganizationQueries
    public interface IOrganizationService
    {
        Task<Result<OrganizationProfileResponse>> UpdateProfileAsync(OrganizationProfileRequest request, CancellationToken ct);
    }
}
