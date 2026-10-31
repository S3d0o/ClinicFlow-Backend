namespace Shared.Authorization
{
    // One role a staff member holds and where (API_CONTRACT.md §13.2 RoleAssignment; docs/v2/adr/0002): the whole
    // organization (BranchIds null) or some branches. From the access token's role_scope claims
    public sealed record StaffAssignment(string Role, IReadOnlySet<int>? BranchIds)
    {
        public bool IsOrganizationWide => BranchIds is null;
    }
}
