namespace Domain.Entities.Subscriptions
{
    // An add-on module a plan includes (Shared.Authorization.Modules). Core modules aren't listed: every plan has them
    public class PlanModule
    {
        public int PlanId { get; set; }
        public string ModuleCode { get; set; } = string.Empty;
    }
}
