namespace Domain.Entities.Tenancy
{
    // A consultation, procedure or triage room at a branch (FR-ORG-04). The queue and the doctor's day point at it
    // from P2. A deactivated room keeps its history and takes no new assignments
    public class Room : BaseEntity<int>, IOrganizationOwned, IBranchOwned
    {
        public int OrganizationId { get; set; }
        public int BranchId { get; set; }
        public string NameEn { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty; // both unique at the branch
        public RoomKind Kind { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
