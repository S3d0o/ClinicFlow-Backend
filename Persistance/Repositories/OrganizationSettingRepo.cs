namespace Persistence.Repositories
{
    public class OrganizationSettingRepo(ClinicDbContext context) : IOrganizationSettingRepo
    {
        public Task<List<OrganizationSetting>> GetAllAsync(CancellationToken ct)
            => context.OrganizationSettings.AsNoTracking().ToListAsync(ct);

        public Task<List<OrganizationSetting>> GetForUpdateAsync(CancellationToken ct)
            => context.OrganizationSettings.ToListAsync(ct);

        public void Add(OrganizationSetting setting) => context.OrganizationSettings.Add(setting);
    }
}
