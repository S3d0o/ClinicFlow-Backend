namespace Domain.Entities.Tenancy
{
    // V2 has exactly one organization, created by the AddOrganizationAndBranches migration (docs/v2/adr/0001)
    public class Organization : BaseEntity<int>
    {
        public const int DefaultId = 1;

        public string LegalName { get; set; } = string.Empty;
        public string BrandName { get; set; } = string.Empty;
        public string? LogoUrl { get; set; }
        public string? TaxRegistrationNo { get; set; }
        public string? CommercialRegistrationNo { get; set; }
        public string DefaultTimeZoneId { get; set; } = string.Empty; // IANA id, e.g. Africa/Cairo
        public bool IsActive { get; set; } = true;
    }
}
