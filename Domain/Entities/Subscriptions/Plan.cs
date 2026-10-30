namespace Domain.Entities.Subscriptions
{
    // A plan the platform sells (docs/v2/adr/0008 decision 6). Plans are data, managed by the platform operator, so
    // they are platform-owned: not organization-owned and not filtered. Code never branches on a plan's code or name
    public class Plan : BaseEntity<int>
    {
        public string Code { get; set; } = string.Empty; // unique, e.g. Starter
        public string Name { get; set; } = string.Empty;
        public bool IsPublic { get; set; } = true;       // offered on the price list, rather than a private deal

        public ICollection<PlanModule> Modules { get; set; } = new List<PlanModule>();
        public ICollection<PlanLimit> Limits { get; set; } = new List<PlanLimit>();
    }
}
