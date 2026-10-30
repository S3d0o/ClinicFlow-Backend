namespace Shared.DTOs.Entitlements
{
    // An organization's subscription as stored: its plan's modules and limits and its module overrides. Not an API
    // response; IEntitlements turns it into the effective modules (Modules.Effective)
    public sealed record OrganizationEntitlementsData(
        string? PlanCode, // null when the organization has no subscription: it has the core modules only
        IReadOnlyList<string> PlanModules,
        IReadOnlyDictionary<string, int> PlanLimits,
        IReadOnlyList<ModuleOverrideData> Overrides)
    {
        public static readonly OrganizationEntitlementsData None = new(null, [], new Dictionary<string, int>(), []);
    }

    public sealed record ModuleOverrideData(string Module, bool Enabled);
}
