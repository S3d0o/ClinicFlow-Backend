namespace Shared.Authorization
{
    // Claims a V2 access token carries besides the id, name, email and role claims (API_CONTRACT.md §2.1). A staff
    // member's token has one role_scope per role: "Manager=*" (the organization) or "Doctor=1,3" (those branches)
    public static class ClinicClaims
    {
        public const string Organization = "org";
        public const string StaffMember = "staff";
        public const string RoleScope = "role_scope";
        public const string MedicalDirector = "med_dir"; // "true" when set

        private const string Everywhere = "*";

        public static string RoleScopeValue(StaffAssignment assignment)
            => $"{assignment.Role}={(assignment.BranchIds is { } branches ? string.Join(',', branches.Order()) : Everywhere)}";

        // Null for a value that isn't role=* or role=ids
        public static StaffAssignment? ParseRoleScope(string value)
        {
            var parts = value.Split('=', 2);
            if (parts.Length != 2 || parts[0].Length == 0)
                return null;
            if (parts[1] == Everywhere)
                return new StaffAssignment(parts[0], null);

            var ids = new HashSet<int>();
            foreach (var id in parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!int.TryParse(id, out var branchId))
                    return null;
                ids.Add(branchId);
            }
            return new StaffAssignment(parts[0], ids);
        }
    }
}
