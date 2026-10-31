namespace Domain.Interfaces.IRepositories
{
    public interface IOrganizationSettingRepo
    {
        // Every saved setting of the organization, untracked, for reading
        Task<List<OrganizationSetting>> GetAllAsync(CancellationToken ct);
        // Tracked, for changing
        Task<List<OrganizationSetting>> GetForUpdateAsync(CancellationToken ct);
        void Add(OrganizationSetting setting);
    }
}
