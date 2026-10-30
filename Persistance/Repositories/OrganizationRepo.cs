namespace Persistence.Repositories
{
    public class OrganizationRepo(ClinicDbContext context) : IOrganizationRepo
    {
        // Organization isn't organization-owned, so no query filter applies: select the tenant's own row
        public Task<Organization> GetCurrentAsync(CancellationToken ct)
            => context.Organizations.SingleAsync(o => o.Id == context.OrganizationId, ct);
    }
}
