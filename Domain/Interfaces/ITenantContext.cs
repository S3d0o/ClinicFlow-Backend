namespace Domain.Interfaces
{
    // The organization the current request or job works for (docs/v2/adr/0001). V2 has one, so this is fixed;
    // V3 resolves it per request
    public interface ITenantContext
    {
        int OrganizationId { get; }

        // Where new schedules go until P1 lets staff choose a branch
        int DefaultBranchId { get; }
    }
}
