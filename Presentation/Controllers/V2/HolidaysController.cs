using Presentation.Authorization;
using Services.Abstraction.Contracts;
using Shared.DTOs.Organization;

namespace Presentation.Controllers.V2
{
    // Holidays, added and removed by their own id because one can close every branch (FR-ORG-03; API_CONTRACT.md
    // §13.25). They are listed through their branch
    [Route(RoutePrefix + "/holidays")]
    public class HolidaysController(IBranchCalendarService service) : V2ApiController
    {
        [HttpPost]
        [HasPermission(Permissions.Organization.BranchesManage)]
        [EndpointSummary("Add a holiday")]
        [EndpointDescription(
            "Closes the branch, or every branch without branchId, from `from` to `to` inclusive; a yearly holiday closes " +
            "the same days every year. No slots are made on it, and free ones already published are withdrawn. Returns " +
            "how many booked visits fall on it and need moving. Requires branches.manage for the branch, or held " +
            "organization-wide for every branch.")]
        [ProducesResponseType(typeof(HolidayAddedResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<HolidayAddedResponse>> Add(HolidayRequest request, CancellationToken ct)
            => HandleResult(await service.AddHolidayAsync(request, ct), StatusCodes.Status201Created);

        [HttpDelete("{holidayId:int}")]
        [HasPermission(Permissions.Organization.BranchesManage)]
        [EndpointSummary("Remove a holiday")]
        [EndpointDescription(
            "Slot generation fills its days again on its next run. Requires the same scope as adding it.")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Remove(int holidayId, CancellationToken ct)
            => HandleResult(await service.RemoveHolidayAsync(holidayId, ct));
    }
}
