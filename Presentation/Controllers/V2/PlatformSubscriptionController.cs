using Presentation.Authorization;
using Services.Abstraction.Contracts;
using Shared.DTOs.Entitlements;

namespace Presentation.Controllers.V2
{
    // The platform operator's switches for the organization's subscription (plans/R1.md M1), so a demo can show what
    // each plan unlocks. Until the operator role and console arrive (R4), the clinic's admin holds them, and only on a
    // deployment with Platform:AllowPlanSwitching on (Development by default)
    [Route(RoutePrefix + "/platform/subscription")]
    public class PlatformSubscriptionController(ISubscriptionService service) : V2ApiController
    {
        [HttpPut("plan")]
        [HasPermission(Permissions.Organization.SettingsManage)]
        [EndpointSummary("Switch the organization's plan")]
        [EndpointDescription(
            "Moves the organization to the plan with this code (Starter, Clinic, Full). Overrides the new plan makes " +
            "redundant are dropped. The new modules apply to the next request. 403 Subscription.SwitchingDisabled " +
            "unless the deployment allows plan switching. Requires org.settings.manage.")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> ChangePlan([FromBody] ChangePlanRequest request, CancellationToken ct)
            => HandleResult(await service.ChangePlanAsync(request, ct));

        [HttpPut("modules")]
        [HasPermission(Permissions.Organization.SettingsManage)]
        [EndpointSummary("Switch an add-on on or off")]
        [EndpointDescription(
            "Switches an add-on module on beyond the plan, or off within it; back to what the plan says removes the " +
            "override. Switching on also enables what the add-on needs. 409 Subscription.ModuleRequired while an " +
            "enabled add-on needs the module; 403 Subscription.SwitchingDisabled unless the deployment allows plan " +
            "switching. Requires org.settings.manage.")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        public async Task<IActionResult> SetModule([FromBody] SetModuleRequest request, CancellationToken ct)
            => HandleResult(await service.SetModuleAsync(request, ct));
    }
}
