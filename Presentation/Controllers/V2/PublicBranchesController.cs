using Microsoft.AspNetCore.Authorization;
using Services.Abstraction.Contracts;
using Shared.DTOs.Organization;

namespace Presentation.Controllers.V2
{
    // The branches the public site and booking show (API_CONTRACT.md §13.5)
    [Route(RoutePrefix + "/public/branches")]
    public class PublicBranchesController(IOrganizationQueries queries) : V2ApiController
    {
        [HttpGet]
        [AllowAnonymous]
        [EndpointSummary("List the active branches")]
        [EndpointDescription("The organization's active branches, by code, with both names and addresses. Public.")]
        [ProducesResponseType(typeof(IReadOnlyList<BranchResponse>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IReadOnlyList<BranchResponse>>> List(CancellationToken ct)
            => HandleResult(await queries.ListPublicBranchesAsync(ct));
    }
}
