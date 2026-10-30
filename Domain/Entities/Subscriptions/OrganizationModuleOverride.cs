namespace Domain.Entities.Subscriptions
{
    // An add-on switched on beyond the plan (an extra or a deal) or switched off within it, for one organization
    // (docs/v2/adr/0008 decision 6). Keyed by organization and module. Audited
    public class OrganizationModuleOverride : IOrganizationOwned
    {
        public int OrganizationId { get; set; }
        public string ModuleCode { get; set; } = string.Empty;
        public bool Enabled { get; set; }
    }
}
