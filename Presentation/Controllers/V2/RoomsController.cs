using Presentation.Authorization;
using Services.Abstraction.Contracts;
using Shared.DTOs.Organization;

namespace Presentation.Controllers.V2
{
    // A room, edited by its own id (FR-ORG-04; API_CONTRACT.md §13.25). Rooms are added through their branch
    [Route(RoutePrefix + "/rooms")]
    public class RoomsController(IBranchService service) : V2ApiController
    {
        [HttpPut("{roomId:int}")]
        [HasPermission(Permissions.Organization.RoomsManage)]
        [EndpointSummary("Update a room")]
        [EndpointDescription(
            "Replaces the room's names and kind, and switches it on or off with isActive. Requires rooms.manage for " +
            "the room's branch; a room outside it is reported as not found.")]
        [ProducesResponseType(typeof(RoomResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        public async Task<ActionResult<RoomResponse>> Update(int roomId, RoomRequest request, CancellationToken ct)
            => HandleResult(await service.UpdateRoomAsync(roomId, request, ct));
    }
}
