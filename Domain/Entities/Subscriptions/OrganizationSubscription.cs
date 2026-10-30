namespace Domain.Entities.Subscriptions
{
    // The plan an organization is on: one row per organization, keyed by it (docs/v2/adr/0008 decision 6). Audited.
    // IEntitlements is the only reader, and a save that changes the row refreshes the organization's cached entitlements
    public class OrganizationSubscription : IOrganizationOwned
    {
        public int OrganizationId { get; set; }
        public int PlanId { get; set; }
        public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Active;
        public DateTime? TrialEndsAt { get; set; }      // UTC
        public DateTime? CurrentPeriodEnd { get; set; } // UTC

        public Plan Plan { get; set; } = null!;
    }
}
