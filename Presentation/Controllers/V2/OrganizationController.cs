using Presentation.Authorization;
using Services.Abstraction.Contracts;
using Shared.DTOs.Organization;

namespace Presentation.Controllers.V2
{
    // The organization's profile (FR-ORG-01; API_CONTRACT.md §13.24). Settings come with P1 PR 3
    [Route(RoutePrefix + "/organization")]
    public class OrganizationController(IOrganizationService service, IOrganizationQueries queries) : V2ApiController
    {
        [HttpGet]
        [HasPermission(Permissions.Organization.SettingsManage)]
        [EndpointSummary("Get the organization's profile")]
        [EndpointDescription(
            "The legal name, brand names in both languages, tax number, commercial registration, phone and email shown " +
            "on invoices, receipts and prescriptions. Requires org.settings.manage.")]
        [ProducesResponseType(typeof(OrganizationProfileResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        public async Task<ActionResult<OrganizationProfileResponse>> GetProfile(CancellationToken ct)
            => HandleResult(await queries.GetProfileAsync(ct));

        [HttpPut]
        [HasPermission(Permissions.Organization.SettingsManage)]
        [EndpointSummary("Update the organization's profile")]
        [EndpointDescription("Replaces the whole profile. The change is audited. Requires org.settings.manage.")]
        [ProducesResponseType(typeof(OrganizationProfileResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        public async Task<ActionResult<OrganizationProfileResponse>> UpdateProfile(OrganizationProfileRequest request, CancellationToken ct)
            => HandleResult(await service.UpdateProfileAsync(request, ct));
    }
}
