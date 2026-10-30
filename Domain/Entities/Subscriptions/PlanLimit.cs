namespace Domain.Entities.Subscriptions
{
    // A counted limit or metered quota of a plan (docs/v2/adr/0008 decision 5), keyed by Shared.Authorization.Limits
    public class PlanLimit
    {
        public int PlanId { get; set; }
        public string LimitKey { get; set; } = string.Empty;
        public int Value { get; set; }
    }
}
