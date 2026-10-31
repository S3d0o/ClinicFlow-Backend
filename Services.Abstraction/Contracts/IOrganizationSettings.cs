using Shared.DTOs.Organization;

namespace Services.Abstraction.Contracts
{
    // The current organization's settings (FR-ORG-05), every key with its default filled in, cached. For the rules
    // that read them (the appointment windows) and for GET /api/v2/organization/settings
    public interface IOrganizationSettings
    {
        Task<OrganizationSettingsResponse> GetAsync(CancellationToken ct);
    }
}
