namespace Domain.Entities.Tenancy
{
    // Operational records also belong to a branch, which permission scopes check (docs/v2/05_DOMAIN_MODEL.md D3)
    public interface IBranchOwned
    {
        int BranchId { get; set; }
    }
}
