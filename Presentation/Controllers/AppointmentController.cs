using Presentation.Extensions;
using Microsoft.AspNetCore.Authorization;
using Services.Abstraction.Contracts;
using Shared.DTOs.Appointment;
using System.Security.Claims;

namespace Presentation.Controllers;

[Route("api/appointments")]
public class AppointmentController(
    IAppointmentBookingService booking,
    IAppointmentCancellationService cancellation,
    IAppointmentVisitService visits,
    IAppointmentQueries queries) : ApiController
{
    /// <summary>Book an appointment</summary>
    /// <remarks>
    /// Patient books an available slot. Slot must be Available and in the future.
    /// Atomic check prevents race conditions — two patients cannot book the same slot.
    /// </remarks>
    [Authorize(Roles = Roles.Patient)]
    [HttpPost]
    [ProducesResponseType(typeof(AppointmentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AppointmentResponse>> Book(
        [FromBody] BookAppointmentRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var result = await booking.BookAppointmentAsync(userId, request, ct);
        return HandleResult(result, StatusCodes.Status201Created);
    }

    /// <summary>Get patient appointment history</summary>
    /// <remarks>
    /// Returns the authenticated patient's appointments.
    /// Filter by Status. Paginated via PageNumber and PageSize.
    /// </remarks>
    [Authorize(Roles = Roles.Patient)]
    [HttpGet("my")]
    [ProducesResponseType(typeof(List<AppointmentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<AppointmentResponse>>> GetPatientAppointments(
        [FromQuery] AppointmentFilterRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var result = await queries.GetPatientAppointmentsAsync(userId, request, ct);
        return HandleResult(result);
    }

    /// <summary>Get doctor appointment list</summary>
    /// <remarks>
    /// Returns the authenticated doctor's appointments.
    /// Filter by Status. Paginated via PageNumber and PageSize.
    /// </remarks>
    [Authorize(Roles = Roles.Doctor)]
    [HttpGet("doctor/my")]
    [ProducesResponseType(typeof(List<AppointmentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<AppointmentResponse>>> GetDoctorAppointments(
        [FromQuery] AppointmentFilterRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var result = await queries.GetDoctorAppointmentsAsync(userId, request, ct);
        return HandleResult(result);
    }

    /// <summary>Get appointment by ID</summary>
    /// <remarks>Returns full appointment details. Only the owning patient, the owning doctor or an admin can access.</remarks>
    [Authorize]
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(AppointmentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AppointmentResponse>> GetById(int id, CancellationToken ct)
    {
        var result = await queries.GetAppointmentAsync(id, User.GetUserId(), User.GetRole(), ct);
        return HandleResult(result);
    }

    /// <summary>Cancel an appointment</summary>
    /// <remarks>
    /// Patient: can only cancel their own appointment, more than 2 hours before start time.
    /// Doctor: can cancel any of their own appointments with no time restriction.
    /// Admin: can cancel any appointment with no restriction.
    /// Slot is freed back to Available after cancellation.
    /// </remarks>
    [Authorize]
    [HttpPost("{id}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(
        int id, [FromBody] CancelAppointmentRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var role = User.GetRole();

        return role switch
        {
            Roles.Patient => HandleResult(await cancellation.CancelByPatientAsync(userId, id, request, ct)),
            Roles.Doctor => HandleResult(await cancellation.CancelByDoctorAsync(userId, id, request, ct)),
            Roles.Admin => HandleResult(await cancellation.CancelByAdminAsync(id, request, ct)),
            _ => Forbid()
        };
    }

    /// <summary>Complete an appointment</summary>
    /// <remarks>
    /// Doctor marks the appointment as completed after the visit.
    /// Only the doctor who owns the appointment can complete it.
    /// Patient can leave a review once the appointment is completed.
    /// </remarks>
    [Authorize(Roles = Roles.Doctor)]
    [HttpPost("{id}/complete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Complete(int id, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var result = await visits.CompleteAppointmentAsync(userId, id, ct);
        return HandleResult(result);
    }

    /// <summary>Mark an appointment as a no-show</summary>
    /// <remarks>
    /// The owning doctor or an admin marks a Confirmed appointment as missed, at least 15 minutes
    /// after it was due to start. Cancelled, completed and already-marked appointments return 409.
    /// </remarks>
    [Authorize(Roles = Roles.Doctor + "," + Roles.Admin)]
    [HttpPost("{id}/no-show")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> MarkNoShow(int id, CancellationToken ct)
        => HandleResult(await visits.MarkNoShowAsync(User.GetUserId(), User.GetRole(), id, ct));

    /// <summary>Add doctor notes</summary>
    /// <remarks>
    /// Doctor adds clinical notes to an appointment.
    /// Can only be added to Confirmed or Completed appointments — not Cancelled or Pending.
    /// Only the owning doctor can add notes.
    /// </remarks>
    [Authorize(Roles = Roles.Doctor)]
    [HttpPut("{id}/notes")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddNotes(
        int id, [FromBody] DoctorNotesRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var result = await visits.AddDoctorNotesAsync(userId, id, request, ct);
        return HandleResult(result);
    }
}