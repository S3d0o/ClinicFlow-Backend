using Microsoft.AspNetCore.Authorization;
using Presentation.Authorization;
using Services.Abstraction.Contracts;
using Shared.DTOs.Organization;

namespace Presentation.Controllers.V2
{
    // The organization's profile and settings (FR-ORG-01, 05; API_CONTRACT.md §13.24)
    [Route(RoutePrefix + "/organization")]
    public class OrganizationController(
        IOrganizationService service,
        IOrganizationQueries queries,
        IOrganizationSettings settings,
        IOrganizationSettingsService settingsService) : V2ApiController
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

        [HttpGet("settings")]
        [Authorize(Roles = Roles.AnyStaff)]
        [EndpointSummary("Get the organization's settings")]
        [EndpointDescription(
            "Every setting, with its default when never changed: the patient's cancellation and reschedule windows, the " +
            "no-show grace, the walk-in cap, fees and deposits, discount caps and approval thresholds, record sharing and " +
            "recruitment periods. Money is EGP. Any staff member.")]
        [ProducesResponseType(typeof(OrganizationSettingsResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        public async Task<ActionResult<OrganizationSettingsResponse>> GetSettings(CancellationToken ct)
            => HandleResult<OrganizationSettingsResponse>(await settings.GetAsync(ct));

        [HttpPatch("settings")]
        [HasPermission(Permissions.Organization.SettingsManage)]
        [EndpointSummary("Change some of the organization's settings")]
        [EndpointDescription(
            "Saves only the keys sent and returns every setting. Each changed key is audited with its old and new value. " +
            "The appointment rules use the new values at once, for existing appointments too. An unknown key is a 400. " +
            "Requires org.settings.manage.")]
        [ProducesResponseType(typeof(OrganizationSettingsResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        public async Task<ActionResult<OrganizationSettingsResponse>> UpdateSettings(OrganizationSettingsPatchRequest request, CancellationToken ct)
            => HandleResult(await settingsService.UpdateAsync(request, ct));
    }
}
