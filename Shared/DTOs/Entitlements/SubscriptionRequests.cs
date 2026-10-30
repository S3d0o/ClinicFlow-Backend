namespace Shared.DTOs.Entitlements
{
    // PUT /api/v2/platform/subscription/plan: the plan's code (Starter, Clinic, Full)
    public sealed record ChangePlanRequest(string PlanCode);

    // PUT /api/v2/platform/subscription/modules: switch an add-on on or off beyond the plan
    public sealed record SetModuleRequest(string Module, bool Enabled);
}
