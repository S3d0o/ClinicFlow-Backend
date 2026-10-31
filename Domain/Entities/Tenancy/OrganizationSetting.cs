namespace Domain.Entities.Tenancy
{
    // One of the organization's settings (FR-ORG-05), stored only once changed: a key never saved has its default
    // (Shared OrganizationSettingsResponse.Defaults). Key is the contract's name (cancellationWindowHours) and Value
    // its JSON (2, true, 100.5), so a setting added later needs no migration. Audited, key by key
    public class OrganizationSetting : BaseEntity<int>, IOrganizationOwned
    {
        public int OrganizationId { get; set; }
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }
}
