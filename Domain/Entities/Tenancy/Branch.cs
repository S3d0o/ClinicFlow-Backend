namespace Domain.Entities.Tenancy
{
    // One physical location (FR-ORG-02). MAIN is created by the AddOrganizationAndBranches migration; P1 PR 1 adds the
    // rest through BranchService. Its time zone governs slot times (docs/v2/adr/0003). A deactivated branch takes no
    // new bookings, and its records stay readable
    public class Branch : BaseEntity<int>, IOrganizationOwned
    {
        public const int MainId = 1;
        public const string MainCode = "MAIN";

        public int OrganizationId { get; set; }
        public string Code { get; set; } = string.Empty; // unique per organization; appears in invoice numbers
        public string NameAr { get; set; } = string.Empty;
        public string NameEn { get; set; } = string.Empty;
        public string? AddressEn { get; set; }
        public string? AddressAr { get; set; }
        public string? City { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string? Phone { get; set; }
        public string TimeZoneId { get; set; } = string.Empty; // IANA id, e.g. Africa/Cairo
        public bool IsActive { get; set; } = true;
    }
}
