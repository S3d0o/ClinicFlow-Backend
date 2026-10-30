namespace Services.Abstraction.Contracts
{
    // Drops cached entitlements (docs/v2/adr/0008 decision 7). Persistence calls it after a save that changes an
    // organization's subscription or overrides (one organization) or a plan (every organization)
    public interface IEntitlementCache
    {
        void Invalidate(int organizationId);

        void InvalidateAll();
    }
}
