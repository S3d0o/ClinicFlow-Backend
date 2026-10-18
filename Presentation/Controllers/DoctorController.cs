using Presentation.Extensions;
using Microsoft.AspNetCore.Authorization;
using Services.Abstraction.Contracts;
using Shared.DTOs.Review;
using System.Security.Claims;

namespace Presentation.Controllers;

[Route("api/doctors")]
public class DoctorController(IDoctorService service) : ApiController
{
    /// <summary>List doctors</summary>
    /// <remarks>
    /// Returns paginated, approved doctors only.
    /// Filter by SpecialtyId, City. Sort by Rating, Fee, or Experience.
    /// </remarks>
    [AllowAnonymous]
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<DoctorSummaryResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<DoctorSummaryResponse>>> GetAll(
        [FromQuery] DoctorFilterRequest request, CancellationToken ct)
    {
        var result = await service.GetAllAsync(request, ct);
        return HandleResult(result);
    }

    /// <summary>Get doctor by ID</summary>
    /// <remarks>Returns full doctor profile including specialty and rating. 404 if not approved or not found.</remarks>
    [AllowAnonymous]
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(DoctorResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DoctorResponse>> GetById(int id, CancellationToken ct)
    {
        var result = await service.GetByIdAsync(id, ct);
        return HandleResult(result);
    }

    /// <summary>Get available slots</summary>
    /// <remarks>Returns Available slots for a doctor on a specific date. `date` is required (400 without it).</remarks>
    [AllowAnonymous]
    [HttpGet("{id}/slots")]
    [ProducesResponseType(typeof(List<SlotResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<SlotResponse>>> GetAllSlots(
        int id, [FromQuery, BindRequired] DateOnly date, CancellationToken ct) // a missing date used to bind as 0001-01-01 (REVIEW.md NL11)
    {
        var result = await service.GetSlotsByDateAsync(id, date, ct);
        return HandleResult(result);
    }

    /// <summary>Get doctor reviews</summary>
    /// <remarks>Returns visible reviews for a doctor.</remarks>
    [AllowAnonymous]
    [HttpGet("{id}/reviews")]
    [ProducesResponseType(typeof(IEnumerable<ReviewResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IEnumerable<ReviewResponse>>> GetAllReviews(
        int id, [FromQuery] ReviewFilterRequest request, CancellationToken ct)
    {
        var result = await service.GetReviewsAsync(id, request, ct);
        return HandleResult(result);
    }

    /// <summary>Update doctor profile</summary>
    /// <remarks>
    /// Updates the authenticated doctor's own profile.
    /// Updatable fields: Bio, ConsultationFee, ClinicAddress, ClinicCity.
    /// Specialty and approval status cannot be changed through this endpoint.
    /// </remarks>
    [Authorize(Roles = "Doctor")]
    [HttpPut("profile")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateProfile(
        [FromBody] UpdateDoctorProfileRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var result = await service.UpdateDoctorProfileAsync(userId, request, ct);
        return HandleResult(result);
    }


    /// <summary>
    ///  get doctors schedules
    /// </summary>
    /// <remarks> 
    ///  Get the doctor's own schedules
    /// </remarks>
    [HttpGet("schedule")]
    [Authorize(Roles = "Doctor")]
    public async Task<ActionResult<IEnumerable<ScheduleResponse>>> GetMySchedules(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var result = await service.GetMySchedulesAsync(userId, ct);
        return HandleResult(result);
    }

    /// <summary>Create schedule day</summary>
    /// <remarks>
    /// Creates a weekly recurring schedule for one day. Generates appointment slots
    /// for the next 14 days immediately. One schedule per day of week — duplicate
    /// days are rejected.
    /// </remarks>
    [Authorize(Roles = "Doctor")]
    [HttpPost("schedule")]
    [ProducesResponseType(typeof(ScheduleResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ScheduleResponse>> CreateSchedule(
        [FromBody] CreateScheduleRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var result = await service.CreateScheduleAsync(userId, request, ct);
        return HandleResult(result, StatusCodes.Status201Created);
    }

    /// <summary>Update schedule day</summary>
    /// <remarks>
    /// Updates an existing schedule's hours, slot duration, or active status.
    /// Future free slots are deleted and regenerated with the new times, in one transaction.
    /// Booked slots, and slots a cancelled appointment points to, are kept; new slots skip times that overlap them.
    /// </remarks>
    [Authorize(Roles = "Doctor")]
    [HttpPut("schedule/{id}")]
    [ProducesResponseType(typeof(ScheduleResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ScheduleResponse>> UpdateSchedule(
        int id, [FromBody] UpdateScheduleRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var result = await service.UpdateScheduleAsync(id, userId, request, ct);
        return HandleResult(result);
    }

    /// <summary>Delete schedule day</summary>
    /// <remarks>
    /// Removes the schedule and its free slots. Returns 409 while upcoming appointments are booked on it.
    /// A schedule whose slots have past appointments is deactivated instead, to keep that history.
    /// </remarks>
    [Authorize(Roles = "Doctor")]
    [HttpDelete("schedule/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteSchedule(int id, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var result = await service.DeleteScheduleAsync(id, userId, ct);
        return HandleResult(result);
    }
}