using Domain.Entities.IdentityModule;
using Domain.Entities.Staff;
using Domain.Enums;
using Microsoft.AspNetCore.Identity;

namespace Services.Implementations
{
    // A staff member's roles come from the assignments of their staff record (P1 PR 4, ADR 0002); a deactivated or
    // invited member has none. A user without a staff record (a patient, or an account made before it had one) keeps
    // the Identity roles. A staff member who is also a patient keeps the Patient role
    public class UserAccessService(
        IUnitOfWork uow,
        UserManager<ApplicationUser> userManager,
        ITenantContext tenant) : IUserAccessService
    {
        public async Task<UserAccess> GetAsync(ApplicationUser user, CancellationToken ct)
        {
            var identityRoles = await userManager.GetRolesAsync(user);
            var staff = await uow.Staff.GetByUserIdAsync(user.Id, ct);
            if (staff is null)
                return new UserAccess(identityRoles.ToList(), tenant.OrganizationId, null, false, []);

            var active = staff.Status == StaffStatus.Active;
            var assignments = active ? Assignments(staff) : [];
            var roles = assignments.Select(a => a.Role)
                .Concat(identityRoles.Where(r => r == Roles.Patient))
                .Distinct()
                .ToList();
            return new UserAccess(roles, tenant.OrganizationId, staff.Id, active && staff.IsMedicalDirector, assignments);
        }

        // One per role: organization-wide when any of its rows has no branch, otherwise its branches
        private static List<StaffAssignment> Assignments(StaffMember staff)
            => staff.Assignments
                .GroupBy(a => a.Role)
                .Select(g => new StaffAssignment(g.Key,
                    g.Any(a => a.BranchId is null) ? null : g.Select(a => a.BranchId!.Value).ToHashSet()))
                .ToList();
    }
}
