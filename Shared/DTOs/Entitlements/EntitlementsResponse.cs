using Domain.Enums;

namespace Shared.DTOs.Entitlements
{
    // GET /api/v2/organization/entitlements (docs/v2/adr/0008 decision 12): the organization's plan, status, effective
    // modules, overrides and limits with their usage. The frontend's Subscription (features/entitlements)
    public sealed record EntitlementsResponse(
        string PlanCode,
        SubscriptionStatus Status,
        DateTime? TrialEndsAt,      // UTC; set while Status is Trial
        DateTime? CurrentPeriodEnd, // UTC; null until subscription billing (R4) sets periods
        IReadOnlyList<string> Modules, // every enabled module, core included, after the plan, overrides and dependencies
        IReadOnlyList<ModuleOverrideResponse> Overrides,
        IReadOnlyList<LimitUsageResponse> Usage);

    // An add-on switched on beyond the plan, or off within it
    public sealed record ModuleOverrideResponse(string Module, bool Enabled);

    // A limit (Shared.Authorization.Limits) and how much of it is used. Included is null when the plan sets no such limit
    public sealed record LimitUsageResponse(string Key, int? Included, int Used);
}
