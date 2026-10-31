namespace Domain.Entities.Tenancy
{
    // Days a branch is closed (FR-ORG-03). With no BranchId it closes every branch of the organization, so it can't be
    // IBranchOwned. A yearly holiday closes the same days every year, matched by month and day; its range may wrap New
    // Year (30 Dec – 2 Jan)
    public class BranchHoliday : BaseEntity<int>, IOrganizationOwned
    {
        public int OrganizationId { get; set; }
        public int? BranchId { get; set; }
        public string NameEn { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public DateOnly From { get; set; }
        public DateOnly To { get; set; } // inclusive
        public bool RecursYearly { get; set; }

        public bool AppliesTo(int branchId) => BranchId is null || BranchId == branchId;

        public bool Covers(DateOnly date)
        {
            if (!RecursYearly)
                return From <= date && date <= To;

            var day = MonthDay(date);
            var (from, to) = (MonthDay(From), MonthDay(To));
            return from <= to ? day >= from && day <= to : day >= from || day <= to;
        }

        private static int MonthDay(DateOnly date) => date.Month * 100 + date.Day;
    }
}
