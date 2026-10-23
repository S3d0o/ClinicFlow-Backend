namespace Domain.Entities.Tenancy
{
    // One branch (MAIN) until P1 adds branch management. Its time zone governs slot times from P0 PR 6 (docs/v2/adr/0003)
    public class Branch : BaseEntity<int>, IOrganizationOwned
    {
        public const int MainId = 1;
        public const string MainCode = "MAIN";

        public int OrganizationId { get; set; }
        public string Code { get; set; } = string.Empty; // unique per organization; appears in invoice numbers
        public string NameAr { get; set; } = string.Empty;
        public string NameEn { get; set; } = string.Empty;
        public string? Address { get; set; }
        public string? City { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string? Phone { get; set; }
        public string TimeZoneId { get; set; } = string.Empty; // IANA id, e.g. Africa/Cairo
        public bool IsActive { get; set; } = true;
    }
}
