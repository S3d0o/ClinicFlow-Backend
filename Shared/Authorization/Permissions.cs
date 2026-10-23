using System.Reflection;

namespace Shared.Authorization
{
    // The permission catalog (docs/v2/02 §5): what authorization checks, never role names (ADR 0002).
    // PermissionMatrixTests keeps this list equal to the document's
    public static class Permissions
    {
        public static class Organization
        {
            public const string SettingsManage = "org.settings.manage";
            public const string BranchesManage = "branches.manage";
            public const string RoomsManage = "rooms.manage";
        }

        public static class Staff
        {
            public const string View = "staff.view";
            public const string Invite = "staff.invite";
            public const string Deactivate = "staff.deactivate";
            public const string RolesManage = "roles.manage";
        }

        public static class Recruitment
        {
            public const string View = "recruitment.view";
            public const string Manage = "recruitment.manage";
        }

        public static class Catalog
        {
            public const string SpecialtiesManage = "specialties.manage";
            public const string ServicesManage = "services.manage";
            public const string PricesManage = "prices.manage";
        }

        public static class Scheduling
        {
            public const string SchedulesView = "schedules.view";
            public const string SchedulesManage = "schedules.manage";
            public const string SchedulesManageOwn = "schedules.manage_own";
            public const string LeaveRequest = "leave.request";
            public const string LeaveApprove = "leave.approve";
            public const string SlotsBlock = "slots.block";
            public const string SlotsOverbook = "slots.overbook";
        }

        public static class Patients
        {
            public const string View = "patients.view";
            public const string Register = "patients.register";
            public const string Edit = "patients.edit";
            public const string Merge = "patients.merge";
        }

        public static class Appointments
        {
            public const string View = "appointments.view";
            public const string BookOnBehalf = "appointments.book_on_behalf";
            public const string Reschedule = "appointments.reschedule";
            public const string Cancel = "appointments.cancel";
            public const string CheckIn = "appointments.check_in";
            public const string MarkNoShow = "appointments.mark_no_show";
            public const string QueueView = "queue.view";
            public const string QueueManage = "queue.manage";
        }

        public static class Clinical
        {
            public const string EncountersView = "encounters.view";
            public const string EncountersWrite = "encounters.write";
            public const string EncountersSign = "encounters.sign";
            public const string EncountersAddend = "encounters.addend";
            public const string EncountersVoid = "encounters.void";
            public const string VitalsWrite = "vitals.write";
            public const string PrescriptionsWrite = "prescriptions.write";
            public const string AttachmentsUpload = "attachments.upload";
            public const string RecordsBreakGlass = "records.break_glass";
        }

        public static class Billing
        {
            public const string InvoicesView = "invoices.view";
            public const string InvoicesCreate = "invoices.create";
            public const string InvoicesDiscount = "invoices.discount";
            public const string InvoicesVoid = "invoices.void";
            public const string PaymentsCollect = "payments.collect";
            public const string RefundsRequest = "refunds.request";
            public const string RefundsApprove = "refunds.approve";
            public const string CashSessionsOperate = "cash_sessions.operate";
            public const string CashSessionsReconcile = "cash_sessions.reconcile";
        }

        public static class Insurance
        {
            public const string PayersManage = "payers.manage";
            public const string PoliciesManage = "policies.manage";
            public const string PreauthManage = "preauth.manage";
            public const string ClaimsManage = "claims.manage";
        }

        public static class Reporting
        {
            public const string Operational = "reports.operational";
            public const string ClinicalReports = "reports.clinical";
            public const string Financial = "reports.financial";
        }

        public static class Platform // "System" in the catalog, renamed so it does not hide the System namespace
        {
            public const string AuditView = "audit.view";
            public const string IntegrationsManage = "integrations.manage";
            public const string NotificationTemplates = "notifications.templates";
        }

        public static readonly IReadOnlyList<string> All = typeof(Permissions).GetNestedTypes()
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        // Clinical content: never granted to Admin or Accountant, by the default matrix or by any later
        // configuration (ADR 0002 decision 6, 02 §4)
        public static readonly IReadOnlySet<string> ClinicalContent = typeof(Clinical)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet();
    }
}
