using Microsoft.AspNetCore.Authorization;
using Services.Abstraction.Contracts;
using Shared.DTOs.Me;

namespace Presentation.Controllers.V2
{
    // The signed-in user (API_CONTRACT.md §13.4). Changing the profile, phone, email and password comes with P1 PR 9–10
    [Route(RoutePrefix + "/me")]
    public class MeController(IMeService service) : V2ApiController
    {
        [HttpGet]
        [Authorize]
        [EndpointSummary("Get the signed-in user")]
        [EndpointDescription(
            "Who the caller is: name, phone, email, their roles with where each applies, and every permission they hold " +
            "with its scope (and branches), for building menus. Roles and permissions are the access token's, so a role " +
            "change shows after the next refresh. Any signed-in user.")]
        [ProducesResponseType(typeof(MeResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<MeResponse>> Get(CancellationToken ct)
            => HandleResult(await service.GetAsync(ct));
    }
}
