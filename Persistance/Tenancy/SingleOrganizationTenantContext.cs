using Domain.Interfaces;

namespace Persistence.Tenancy
{
    // V2 serves one organization with one default branch, both created by the AddOrganizationAndBranches migration
    // (docs/v2/adr/0001). V3 replaces this with a per-request resolver
    public sealed class SingleOrganizationTenantContext : ITenantContext
    {
        public int OrganizationId => Organization.DefaultId;

        public int DefaultBranchId => Branch.MainId;
    }
}
