namespace Domain.Entities.Staff
{
    // One role of a staff member at one branch, or across the organization when BranchId is null (02 §3). A role at
    // several branches is several rows. Not IBranchOwned, since the organization-wide row has no branch
    public class StaffRoleAssignment : BaseEntity<int>, IOrganizationOwned
    {
        public int OrganizationId { get; set; }
        public int StaffMemberId { get; set; }
        public string Role { get; set; } = string.Empty; // Shared.Authorization.Roles, never Patient
        public int? BranchId { get; set; }
    }
}
