using Microsoft.AspNetCore.Authorization;
using Presentation.Authorization;
using Services.Abstraction.Contracts;
using Shared.DTOs.Entitlements;

namespace Presentation.Controllers.V2
{
    // What the organization's subscription includes (docs/v2/adr/0008 decision 12). The backend enforces it on every
    // [HasPermission] endpoint; the frontend uses these reads to hide what the plan doesn't include
    [Route(RoutePrefix)]
    public class EntitlementsController(ISubscriptionQueries queries) : V2ApiController
    {
        [HttpGet("organization/entitlements")]
        [HasPermission(Permissions.Organization.SettingsManage)]
        [EndpointSummary("Get the organization's plan, modules and limits")]
        [EndpointDescription(
            "The organization's plan code, subscription status and period, every enabled module (core modules, the " +
            "plan's add-ons and overrides, closed over dependencies), the add-on overrides, and each limit with its " +
            "usage (included is null when the plan sets no such limit). Requires org.settings.manage.")]
        [ProducesResponseType(typeof(EntitlementsResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<EntitlementsResponse>> GetEntitlements(CancellationToken ct)
            => HandleResult(await queries.GetEntitlementsAsync(ct));

        [HttpGet("organization/modules")]
        [AllowAnonymous]
        [EndpointSummary("List the organization's enabled modules")]
        [EndpointDescription(
            "The codes of every enabled module, core included, in catalog order. Public: patients' and visitors' " +
            "pages follow the plan too.")]
        [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IReadOnlyList<string>>> GetEnabledModules(CancellationToken ct)
            => HandleResult(await queries.GetEnabledModulesAsync(ct));

        [HttpGet("plans")]
        [HasPermission(Permissions.Organization.SettingsManage)]
        [EndpointSummary("List the plans")]
        [EndpointDescription(
            "The plans on the platform's price list, each with the add-ons it includes (core modules come with every " +
            "plan) and its limits (null when the plan sets none). Requires org.settings.manage.")]
        [ProducesResponseType(typeof(IReadOnlyList<PlanResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        public async Task<ActionResult<IReadOnlyList<PlanResponse>>> ListPlans(CancellationToken ct)
            => HandleResult(await queries.ListPlansAsync(ct));
    }
}
