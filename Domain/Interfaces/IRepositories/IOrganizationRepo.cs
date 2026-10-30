namespace Domain.Interfaces.IRepositories
{
    public interface IOrganizationRepo
    {
        // The current user's organization (ITenantContext), tracked, for OrganizationService's changes
        Task<Organization> GetCurrentAsync(CancellationToken ct);
    }
}
