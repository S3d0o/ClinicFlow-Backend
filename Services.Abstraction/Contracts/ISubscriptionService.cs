using Shared.DTOs.Entitlements;

namespace Services.Abstraction.Contracts
{
    // The platform operator's switches for the current organization (docs/v2/adr/0008; plans/R1.md M1: a route to
    // switch the plan, until the operator console arrives in R4). Refused unless Platform:AllowPlanSwitching is on
    public interface ISubscriptionService
    {
        // Moves the organization to the plan. Overrides the new plan makes redundant are dropped
        Task<Result> ChangePlanAsync(ChangePlanRequest request, CancellationToken ct);

        // Switches an add-on on or off beyond the plan; back to what the plan says removes the override. Switching
        // one off that an enabled add-on needs is refused
        Task<Result> SetModuleAsync(SetModuleRequest request, CancellationToken ct);
    }
}
