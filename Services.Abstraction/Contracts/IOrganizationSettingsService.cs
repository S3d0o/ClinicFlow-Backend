using Shared.DTOs.Organization;

namespace Services.Abstraction.Contracts
{
    // Changes to the organization's settings (FR-ORG-05; API_CONTRACT.md §13.24). Reads are IOrganizationSettings
    public interface IOrganizationSettingsService
    {
        // org.settings.manage. Saves only the keys sent (and only those that changed, so each audit entry is a real
        // change), then returns every setting
        Task<Result<OrganizationSettingsResponse>> UpdateAsync(OrganizationSettingsPatchRequest request, CancellationToken ct);
    }
}
