namespace Shared.Authorization
{
    // The default role × permission matrix (docs/v2/02 §6), in code until roles.manage makes it editable per
    // organization (P1). PermissionMatrixTests compares it with the document, cell by cell.
    //
    // Reading a cell: O = Organization, B = Branch, S = Own, _ = not granted. A Manager is assigned to a branch
    // or to the organization; the matrix gives the Branch scope and P1's staff assignment widens it. Qualifiers in
    // the document (limits, approver or requester, "rules apply") are rules of the feature that uses the
    // permission, not part of the grant. Cells that depend on who the manager is rather than on the role
    // ("org managers only", "medical director") are left out: P1 grants them through the staff member's flags
    public static class DefaultPermissionMatrix
    {
        private static readonly PermissionScope? O = PermissionScope.Organization, B = PermissionScope.Branch, S = PermissionScope.Own, _ = null;

        // Columns in the order of Roles.All: Admin, Manager, Receptionist, Nurse, Doctor, Accountant, Patient
        private static readonly Dictionary<string, PermissionScope?[]> Rows = new()
        {
            [Permissions.Organization.SettingsManage] = [O, _, _, _, _, _, _],
            [Permissions.Organization.BranchesManage] = [O, B, _, _, _, _, _],
            [Permissions.Organization.RoomsManage] = [_, B, _, _, _, _, _],
            [Permissions.Staff.View] = [O, B, B, B, B, B, _],
            [Permissions.Staff.Invite] = [O, B, _, _, _, _, _],
            [Permissions.Staff.Deactivate] = [O, B, _, _, _, _, _],
            [Permissions.Staff.RolesManage] = [O, _, _, _, _, _, _],
            [Permissions.Recruitment.View] = [_, B, _, _, _, _, _],
            [Permissions.Recruitment.Manage] = [_, B, _, _, _, _, _],
            [Permissions.Catalog.SpecialtiesManage] = [O, O, _, _, _, _, _],
            [Permissions.Catalog.ServicesManage] = [_, B, _, _, _, _, _],
            [Permissions.Catalog.PricesManage] = [_, B, _, _, _, O, _],
            [Permissions.Scheduling.SchedulesView] = [_, B, B, B, B, _, _], // patients see public slots, with no permission
            [Permissions.Scheduling.SchedulesManage] = [_, B, _, _, _, _, _],
            [Permissions.Scheduling.SchedulesManageOwn] = [_, _, _, _, S, _, _],
            [Permissions.Scheduling.LeaveRequest] = [_, _, _, _, S, _, _],
            [Permissions.Scheduling.LeaveApprove] = [_, B, _, _, _, _, _],
            [Permissions.Scheduling.SlotsBlock] = [_, B, B, _, S, _, _],
            [Permissions.Scheduling.SlotsOverbook] = [_, B, _, _, S, _, _],
            [Permissions.Patients.View] = [_, B, B, B, B, B, S],
            [Permissions.Patients.Register] = [_, B, B, _, _, _, S],
            [Permissions.Patients.Edit] = [_, B, B, _, _, _, S],
            [Permissions.Patients.Merge] = [_, O, _, _, _, _, _],
            [Permissions.Appointments.View] = [_, B, B, B, S, B, S],
            [Permissions.Appointments.BookOnBehalf] = [_, B, B, _, _, _, _],
            [Permissions.Appointments.Reschedule] = [_, B, B, _, S, _, S],
            [Permissions.Appointments.Cancel] = [_, B, B, _, S, _, S],
            [Permissions.Appointments.CheckIn] = [_, B, B, B, _, _, _],
            [Permissions.Appointments.MarkNoShow] = [_, B, B, _, S, _, _],
            [Permissions.Appointments.QueueView] = [_, B, B, B, B, _, S],
            [Permissions.Appointments.QueueManage] = [_, B, B, B, S, _, _],
            [Permissions.Clinical.EncountersView] = [_, _, _, B, B, _, S],
            [Permissions.Clinical.EncountersWrite] = [_, _, _, _, S, _, _],
            [Permissions.Clinical.EncountersSign] = [_, _, _, _, S, _, _],
            [Permissions.Clinical.EncountersAddend] = [_, _, _, _, S, _, _],
            [Permissions.Clinical.EncountersVoid] = [_, O, _, _, S, _, _],
            [Permissions.Clinical.VitalsWrite] = [_, _, _, B, B, _, _],
            [Permissions.Clinical.PrescriptionsWrite] = [_, _, _, _, S, _, _],
            [Permissions.Clinical.AttachmentsUpload] = [_, _, B, B, B, _, S],
            [Permissions.Clinical.RecordsBreakGlass] = [_, _, _, _, B, _, _],
            [Permissions.Billing.InvoicesView] = [_, B, B, _, S, B, S],
            [Permissions.Billing.InvoicesCreate] = [_, B, B, _, _, B, _],
            [Permissions.Billing.InvoicesDiscount] = [_, B, B, _, _, B, _],
            [Permissions.Billing.InvoicesVoid] = [_, B, _, _, _, B, _],
            [Permissions.Billing.PaymentsCollect] = [_, B, B, _, _, B, S],
            [Permissions.Billing.RefundsRequest] = [_, B, B, _, _, B, _],
            [Permissions.Billing.RefundsApprove] = [_, B, _, _, _, O, _],
            [Permissions.Billing.CashSessionsOperate] = [_, _, B, _, _, B, _],
            [Permissions.Billing.CashSessionsReconcile] = [_, B, _, _, _, B, _],
            [Permissions.Insurance.PayersManage] = [_, O, _, _, _, O, _],
            [Permissions.Insurance.PoliciesManage] = [_, B, B, _, _, B, S],
            [Permissions.Insurance.PreauthManage] = [_, B, B, _, S, B, _],
            [Permissions.Insurance.ClaimsManage] = [_, O, _, _, _, B, _],
            [Permissions.Reporting.Operational] = [_, B, _, _, S, B, _],
            [Permissions.Reporting.ClinicalReports] = [_, _, _, _, S, _, _],
            [Permissions.Reporting.Financial] = [_, B, _, _, S, B, _],
            [Permissions.Platform.AuditView] = [O, _, _, _, _, _, _],
            [Permissions.Platform.IntegrationsManage] = [O, _, _, _, _, _, _],
            [Permissions.Platform.NotificationTemplates] = [O, O, _, _, _, _, _],
        };

        // role → permission → scope
        public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, PermissionScope>> Grants = Build();

        // The widest scope any of the roles grants the permission in (02 §4: the union of the roles, widest wins),
        // or null. Unknown roles grant nothing
        public static PermissionScope? ScopeFor(IEnumerable<string> roles, string permission)
            => roles
                .Select(role => Grants.TryGetValue(role, out var grants) && grants.TryGetValue(permission, out var scope) ? scope : (PermissionScope?)null)
                .Max();

        // The hard rule no configuration may break (ADR 0002 decision 6). The matrix is checked against it when it
        // is built, and P1's roles.manage will check every change against it too
        public static bool CanGrant(string role, string permission)
            => !(Permissions.ClinicalContent.Contains(permission) && role is Roles.Admin or Roles.Accountant);

        private static Dictionary<string, IReadOnlyDictionary<string, PermissionScope>> Build()
        {
            var grants = Roles.All.ToDictionary(role => role, _ => new Dictionary<string, PermissionScope>());
            foreach (var (permission, cells) in Rows)
            {
                for (var i = 0; i < Roles.All.Count; i++)
                {
                    if (cells[i] is not { } scope) continue;
                    if (!CanGrant(Roles.All[i], permission))
                        throw new InvalidOperationException($"{Roles.All[i]} can't be granted {permission} (ADR 0002 decision 6)");
                    grants[Roles.All[i]][permission] = scope;
                }
            }
            return grants.ToDictionary(g => g.Key, g => (IReadOnlyDictionary<string, PermissionScope>)g.Value);
        }
    }
}
