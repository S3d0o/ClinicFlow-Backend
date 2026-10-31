using Microsoft.AspNetCore.Authorization;
using Presentation.Authorization;
using Services.Abstraction.Contracts;
using Shared.DTOs.Organization;

namespace Presentation.Controllers.V2
{
    // Branches, their working hours, holidays and rooms (FR-ORG-02, 03, 04; API_CONTRACT.md §13.25). Holidays are added
    // and removed through HolidaysController, since one can close every branch
    [Route(RoutePrefix + "/branches")]
    public class BranchesController(IBranchService service, IBranchCalendarService calendar, IOrganizationQueries queries) : V2ApiController
    {
        [HttpGet]
        [Authorize(Roles = Roles.AnyStaff)]
        [EndpointSummary("List the branches")]
        [EndpointDescription("Every branch of the organization, deactivated ones included, by code. Any staff member.")]
        [ProducesResponseType(typeof(IReadOnlyList<BranchResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        public async Task<ActionResult<IReadOnlyList<BranchResponse>>> List(CancellationToken ct)
            => HandleResult(await queries.ListBranchesAsync(ct));

        [HttpPost]
        [HasPermission(Permissions.Organization.BranchesManage)]
        [EndpointSummary("Create a branch")]
        [EndpointDescription(
            "Adds an active branch. The code (2 to 4 capital letters) is unique and appears in invoice numbers. " +
            "Requires branches.manage held organization-wide.")]
        [ProducesResponseType(typeof(BranchResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        public async Task<ActionResult<BranchResponse>> Create(BranchRequest request, CancellationToken ct)
            => HandleResult(await service.CreateAsync(request, ct), StatusCodes.Status201Created);

        [HttpPut("{branchId:int}")]
        [HasPermission(Permissions.Organization.BranchesManage)]
        [EndpointSummary("Update a branch")]
        [EndpointDescription("Replaces the branch's code, names, addresses, phone and time zone. Requires branches.manage for the branch.")]
        [ProducesResponseType(typeof(BranchResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        public async Task<ActionResult<BranchResponse>> Update(int branchId, BranchRequest request, CancellationToken ct)
            => HandleResult(await service.UpdateAsync(branchId, request, ct));

        [HttpPost("{branchId:int}/deactivate")]
        [HasPermission(Permissions.Organization.BranchesManage)]
        [EndpointSummary("Deactivate a branch")]
        [EndpointDescription(
            "The branch takes no new bookings; its records stay readable and booked visits stay. The only active " +
            "branch can't be deactivated. Requires branches.manage held organization-wide.")]
        [ProducesResponseType(typeof(BranchResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        public async Task<ActionResult<BranchResponse>> Deactivate(int branchId, CancellationToken ct)
            => HandleResult(await service.DeactivateAsync(branchId, ct));

        [HttpPost("{branchId:int}/activate")]
        [HasPermission(Permissions.Organization.BranchesManage)]
        [EndpointSummary("Activate a branch")]
        [EndpointDescription("The branch takes bookings again. Requires branches.manage held organization-wide.")]
        [ProducesResponseType(typeof(BranchResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<BranchResponse>> Activate(int branchId, CancellationToken ct)
            => HandleResult(await service.ActivateAsync(branchId, ct));

        [HttpGet("{branchId:int}/working-hours")]
        [Authorize(Roles = Roles.AnyStaff)]
        [EndpointSummary("Get a branch's working hours")]
        [EndpointDescription(
            "Seven days, Sunday (0) first, each with its opening periods in the branch's wall clock; a day with none is " +
            "closed. Any staff member.")]
        [ProducesResponseType(typeof(IReadOnlyList<DayHoursResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<IReadOnlyList<DayHoursResponse>>> GetWorkingHours(int branchId, CancellationToken ct)
            => HandleResult(await queries.GetWorkingHoursAsync(branchId, ct));

        [HttpPut("{branchId:int}/working-hours")]
        [HasPermission(Permissions.Organization.BranchesManage)]
        [EndpointSummary("Set a branch's working hours")]
        [EndpointDescription(
            "Replaces the week; a weekday not sent is closed. New slots are generated only inside the opening periods; " +
            "existing slots stay. Requires branches.manage for the branch.")]
        [ProducesResponseType(typeof(IReadOnlyList<DayHoursResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<IReadOnlyList<DayHoursResponse>>> SetWorkingHours(
            int branchId, IReadOnlyList<DayHoursRequest> week, CancellationToken ct)
            => HandleResult(await calendar.SetWorkingHoursAsync(branchId, week, ct));

        [HttpGet("{branchId:int}/holidays")]
        [Authorize(Roles = Roles.AnyStaff)]
        [EndpointSummary("List a branch's holidays")]
        [EndpointDescription(
            "The branch's holidays and those that close every branch, by start date. With year, those that fall in it; " +
            "without, this year's and later. Yearly holidays are always listed. Any staff member.")]
        [ProducesResponseType(typeof(IReadOnlyList<HolidayResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<IReadOnlyList<HolidayResponse>>> ListHolidays(int branchId, [FromQuery] int? year, CancellationToken ct)
            => HandleResult(await queries.ListHolidaysAsync(branchId, year, ct));

        [HttpGet("{branchId:int}/rooms")]
        [Authorize(Roles = Roles.AnyStaff)]
        [EndpointSummary("List a branch's rooms")]
        [EndpointDescription("The branch's rooms, inactive ones included, by English name. Any staff member.")]
        [ProducesResponseType(typeof(IReadOnlyList<RoomResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<IReadOnlyList<RoomResponse>>> ListRooms(int branchId, CancellationToken ct)
            => HandleResult(await queries.ListRoomsAsync(branchId, ct));

        [HttpPost("{branchId:int}/rooms")]
        [HasPermission(Permissions.Organization.RoomsManage)]
        [EndpointSummary("Add a room to a branch")]
        [EndpointDescription(
            "Adds an active consultation, procedure or triage room. Both names are unique at the branch. Requires " +
            "rooms.manage for the branch.")]
        [ProducesResponseType(typeof(RoomResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        public async Task<ActionResult<RoomResponse>> AddRoom(int branchId, RoomRequest request, CancellationToken ct)
            => HandleResult(await service.AddRoomAsync(branchId, request, ct), StatusCodes.Status201Created);
    }
}
