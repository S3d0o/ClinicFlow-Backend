using System.Collections.Frozen;

namespace Shared.Authorization
{
    // The module catalog (docs/v2/adr/0008 decisions 3–4, 04 §3). The modules are code; which ones a clinic has comes
    // from its plan and overrides, which are data (IEntitlements). Code never branches on a plan name.
    // The frontend mirrors this file in src/features/entitlements/model/modules.ts
    public static class Modules
    {
        // Core: every clinic has these, whatever its plan
        public const string Organization = "ORG";
        public const string Staff = "STF";
        public const string Accounts = "ACC";
        public const string ServicesAndPricing = "SVC";
        public const string Scheduling = "SCH";
        public const string Appointments = "APT";
        public const string Notifications = "NTF";
        public const string Audit = "AUD";

        // Add-ons: switched on by the plan or by an override
        public const string FrontDesk = "FD";
        public const string Billing = "BIL";
        public const string ClinicalRecords = "EMR";
        public const string Insurance = "INS";
        public const string Reporting = "RPT";
        public const string PatientPortal = "PAT";
        public const string Recruitment = "REC";
        public const string Chatbot = "BOT";

        // Every module in catalog order (core first, as in modules.ts), which is the order responses list them in
        public static readonly IReadOnlyList<string> Catalog =
        [
            Organization, Staff, Accounts, ServicesAndPricing, Scheduling, Appointments, Notifications, Audit,
            FrontDesk, Billing, ClinicalRecords, Insurance, Reporting, PatientPortal, Recruitment, Chatbot,
        ];

        public static readonly IReadOnlySet<string> Core = Catalog.Take(8).ToFrozenSet();

        public static readonly IReadOnlySet<string> AddOns = Catalog.Skip(8).ToFrozenSet();

        public static readonly IReadOnlySet<string> All = Catalog.ToFrozenSet();

        // What an add-on needs on as well (decision 4). Enabling a module enables these; switching one off that an
        // enabled module needs is refused where overrides are written
        public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Dependencies =
            new Dictionary<string, IReadOnlyList<string>>
            {
                [Insurance] = [Billing, ClinicalRecords],
                [Chatbot] = [PatientPortal],
            }.ToFrozenDictionary();

        // The module of every permission: the "Module" column of 02 §5, which decision 3 makes authoritative.
        // PermissionMatrixTests compares the two. A grant counts only while its module is on (decision 8)
        private static readonly FrozenDictionary<string, string> ByPermission = new Dictionary<string, string>
        {
            [Permissions.Organization.SettingsManage] = Organization,
            [Permissions.Organization.BranchesManage] = Organization,
            [Permissions.Organization.RoomsManage] = Organization,

            [Permissions.Staff.View] = Staff,
            [Permissions.Staff.Invite] = Staff,
            [Permissions.Staff.Deactivate] = Staff,
            [Permissions.Staff.RolesManage] = Staff,

            [Permissions.Recruitment.View] = Recruitment,
            [Permissions.Recruitment.Manage] = Recruitment,

            [Permissions.Catalog.SpecialtiesManage] = ServicesAndPricing,
            [Permissions.Catalog.ServicesManage] = ServicesAndPricing,
            [Permissions.Catalog.PricesManage] = ServicesAndPricing,

            [Permissions.Scheduling.SchedulesView] = Scheduling,
            [Permissions.Scheduling.SchedulesManage] = Scheduling,
            [Permissions.Scheduling.SchedulesManageOwn] = Scheduling,
            [Permissions.Scheduling.LeaveRequest] = Scheduling,
            [Permissions.Scheduling.LeaveApprove] = Scheduling,
            [Permissions.Scheduling.SlotsBlock] = Scheduling,
            [Permissions.Scheduling.SlotsOverbook] = Scheduling,

            [Permissions.Patients.View] = Appointments,
            [Permissions.Patients.Register] = Appointments,
            [Permissions.Patients.Edit] = Appointments,
            [Permissions.Patients.Merge] = Appointments,

            // The core appointment module covers booking on behalf and check-in, so a clinic without the front desk
            // still runs a calendar; the add-on brings the live queue and triage
            [Permissions.Appointments.View] = Appointments,
            [Permissions.Appointments.BookOnBehalf] = Appointments,
            [Permissions.Appointments.Reschedule] = Appointments,
            [Permissions.Appointments.Cancel] = Appointments,
            [Permissions.Appointments.CheckIn] = Appointments,
            [Permissions.Appointments.MarkNoShow] = Appointments,
            [Permissions.Appointments.QueueView] = FrontDesk,
            [Permissions.Appointments.QueueManage] = FrontDesk,
            [Permissions.Clinical.VitalsWrite] = FrontDesk,

            [Permissions.Clinical.EncountersView] = ClinicalRecords,
            [Permissions.Clinical.EncountersWrite] = ClinicalRecords,
            [Permissions.Clinical.EncountersSign] = ClinicalRecords,
            [Permissions.Clinical.EncountersAddend] = ClinicalRecords,
            [Permissions.Clinical.EncountersVoid] = ClinicalRecords,
            [Permissions.Clinical.PrescriptionsWrite] = ClinicalRecords,
            [Permissions.Clinical.AttachmentsUpload] = ClinicalRecords,
            [Permissions.Clinical.RecordsBreakGlass] = ClinicalRecords,

            [Permissions.Billing.InvoicesView] = Billing,
            [Permissions.Billing.InvoicesCreate] = Billing,
            [Permissions.Billing.InvoicesDiscount] = Billing,
            [Permissions.Billing.InvoicesVoid] = Billing,
            [Permissions.Billing.PaymentsCollect] = Billing,
            [Permissions.Billing.RefundsRequest] = Billing,
            [Permissions.Billing.RefundsApprove] = Billing,
            [Permissions.Billing.CashSessionsOperate] = Billing,
            [Permissions.Billing.CashSessionsReconcile] = Billing,

            [Permissions.Insurance.PayersManage] = Insurance,
            [Permissions.Insurance.PoliciesManage] = Insurance,
            [Permissions.Insurance.PreauthManage] = Insurance,
            [Permissions.Insurance.ClaimsManage] = Insurance,

            // The dashboard is core; the reports beyond it are the RPT add-on, and money reports come with billing
            [Permissions.Reporting.Operational] = Appointments,
            [Permissions.Reporting.ClinicalReports] = Reporting,
            [Permissions.Reporting.Financial] = Billing,

            [Permissions.Platform.AuditView] = Audit,
            [Permissions.Platform.IntegrationsManage] = Notifications,
            [Permissions.Platform.NotificationTemplates] = Notifications,
        }.ToFrozenDictionary();

        public static IReadOnlyDictionary<string, string> OfPermissions => ByPermission;

        // The module a permission belongs to. An unknown permission is a typo and must not quietly pass or fail
        public static string Of(string permission)
            => ByPermission.TryGetValue(permission, out var module)
                ? module
                : throw new InvalidOperationException($"Permission '{permission}' belongs to no module");

        public static bool IsCore(string module) => Core.Contains(module);

        // Effective modules (decision 6) = core ∪ plan ∪ enabled overrides − disabled overrides, closed over
        // dependencies. Overrides apply in order, as the frontend's effectiveModules does. Core modules are always on
        // (decision 4), so an override can't switch one off. The closure runs last: a disabled module that an enabled
        // one needs comes back, since such an override is refused where overrides are written. Unknown codes are dropped
        public static IReadOnlySet<string> Effective(
            IEnumerable<string> planModules, IEnumerable<(string Module, bool Enabled)> overrides)
        {
            var enabled = new HashSet<string>(planModules.Where(AddOns.Contains));
            foreach (var (module, on) in overrides)
            {
                if (!AddOns.Contains(module))
                    continue;
                if (on)
                    enabled.Add(module);
                else
                    enabled.Remove(module);
            }

            enabled.UnionWith(Core);
            return WithDependencies(enabled);
        }

        // The enabled add-ons that need the module, in catalog order: while any is on, the module can't be switched off
        // (decision 4; the frontend's requiredBy)
        public static IReadOnlyList<string> RequiredBy(string module, IReadOnlySet<string> enabled)
            => Catalog
                .Where(other => enabled.Contains(other) && Dependencies.TryGetValue(other, out var needed) && needed.Contains(module))
                .ToList();

        // The modules in catalog order; unknown codes are dropped
        public static IReadOnlyList<string> InCatalogOrder(IEnumerable<string> modules)
        {
            var set = modules as IReadOnlySet<string> ?? modules.ToHashSet();
            return Catalog.Where(set.Contains).ToList();
        }

        // A module's position in the catalog, for sorting; unknown codes sort last
        public static int OrderOf(string module) => Positions.GetValueOrDefault(module, int.MaxValue);

        private static readonly FrozenDictionary<string, int> Positions =
            Catalog.Select((module, i) => (module, i)).ToFrozenDictionary(p => p.module, p => p.i);

        // The modules and everything they need, transitively
        public static IReadOnlySet<string> WithDependencies(IEnumerable<string> modules)
        {
            var closed = new HashSet<string>(modules);
            var pending = new Stack<string>(closed);
            while (pending.TryPop(out var module))
            {
                if (!Dependencies.TryGetValue(module, out var needed))
                    continue;
                foreach (var dependency in needed)
                    if (closed.Add(dependency))
                        pending.Push(dependency);
            }

            return closed.ToFrozenSet();
        }
    }
}
