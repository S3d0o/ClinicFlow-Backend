namespace Shared.DTOs.Entitlements
{
    // A plan the platform offers (GET /api/v2/plans): the add-ons it includes (core modules come with every plan) and
    // its limits. The frontend's Plan. Prices wait for ADR 0008 Q19
    public sealed record PlanResponse(string Code, string Name, IReadOnlyList<string> Modules, PlanLimitsResponse Limits);

    // A plan's limits by Shared.Authorization.Limits key; null when the plan sets none
    public sealed record PlanLimitsResponse(int? Branches, int? Doctors, int? PhoneMessagesPerMonth);
}
