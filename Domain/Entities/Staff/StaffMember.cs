using Domain.Entities.IdentityModule;

namespace Domain.Entities.Staff
{
    // A person who works for the organization (FR-STF-08; docs/v2/adr/0002): their user account plus where they work
    // and in which roles. Roles in the access token come from the assignments of an Active member; the Identity role
    // rows of Admin and Doctor users were backfilled into staff members (migration AddStaffMembers) and stopped being
    // the source. Patients aren't staff. One per user and organization
    public class StaffMember : BaseEntity<int>, IOrganizationOwned
    {
        public int OrganizationId { get; set; }
        public Guid UserId { get; set; }
        public StaffStatus Status { get; set; } = StaffStatus.Active;
        public DateOnly? StartDate { get; set; }
        public DateOnly? EndDate { get; set; }  // access ends after this day in the branch's calendar (P1 PR 7)
        public bool IsMedicalDirector { get; set; } // a flag, not a role: it only widens clinical access (02 §6)

        public ApplicationUser User { get; set; } = null!;
        public List<StaffRoleAssignment> Assignments { get; set; } = [];
    }
}
