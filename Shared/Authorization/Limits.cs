using System.Collections.Frozen;

namespace Shared.Authorization
{
    // The counted limits and metered quotas of a plan (docs/v2/adr/0008 decision 5), stored as PlanLimit.LimitKey. The
    // names match the frontend's PlanLimits keys. Branches and doctors are counted limits (checked at the write that
    // would exceed them, from P1); phone messages are a monthly metered quota (UsageRecord, with the phone channels)
    public static class Limits
    {
        public const string Branches = "branches";
        public const string Doctors = "doctors";
        public const string PhoneMessagesPerMonth = "phoneMessagesPerMonth";

        // In the order responses list them
        public static readonly IReadOnlyList<string> Catalog = [Branches, Doctors, PhoneMessagesPerMonth];

        public static readonly IReadOnlySet<string> All = Catalog.ToFrozenSet();
    }
}
