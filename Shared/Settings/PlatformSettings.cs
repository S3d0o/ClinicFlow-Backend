namespace Shared.Settings
{
    // The platform operator's switches, until the operator console and its role arrive in R4 (docs/v2/adr/0008
    // decision 17, plans/R1.md M1)
    public class PlatformSettings
    {
        // Whether the clinic's admin may switch the organization's plan and add-ons through /api/v2/platform/subscription,
        // so a demo can show what each plan unlocks. Default: true in Development, false otherwise (set by the host).
        // Never enable it on a paying clinic's deployment: it upgrades the plan for free
        public bool AllowPlanSwitching { get; set; }
    }
}
