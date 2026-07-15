using Microsoft.AspNetCore.Authorization;
using Services.Abstraction.Contracts;
using Shared.DTOs.Admin;
using Shared.DTOs.Appointment;
using Shared.DTOs.Specialty;

namespace Presentation.Controllers;

[Route("api/admin")]
[Authorize(Roles = "Admin")]
public class AdminController(
    IAdminService adminService,
    ISpecialtyService specialtyService) : ApiController
{
    // ── Doctor management ─────────────────────────────────────────────────────

    /// <summary>Get pending doctors</summary>
    /// <remarks>Returns all doctors awaiting admin approval.</remarks>
    [HttpGet("doctors/pending")]
    [ProducesResponseType(typeof(List<DoctorResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<DoctorResponse>>> GetPendingDoctors(CancellationToken ct)
        => HandleResult(await adminService.GetPendingDoctorsAsync(ct));

    /// <summary>Approve a doctor</summary>
    /// <remarks>
    /// Approves a pending doctor. Doctor becomes visible in search
    /// and can set their schedule after approval.
    /// </remarks>
    [HttpPost("doctors/{id}/approve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ApproveDoctor(int id, CancellationToken ct)
        => HandleResult(await adminService.ApproveDoctorAsync(id, ct));

    /// <summary>Suspend a doctor</summary>
    /// <remarks>
    /// Suspends a doctor account. Sets IsActive = false on ApplicationUser.
    /// Doctor cannot login after suspension.
    /// </remarks>
    [HttpPost("doctors/{id}/suspend")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SuspendDoctor(int id, CancellationToken ct)
        => HandleResult(await adminService.SuspendDoctorAsync(id, ct));

    // ── Specialty management ──────────────────────────────────────────────────

    /// <summary>Get all specialties</summary>
    /// <remarks>Admin view — includes inactive specialties.</remarks>
    [HttpGet("specialties")]
    [ProducesResponseType(typeof(List<SpecialtyResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<SpecialtyResponse>>> GetSpecialties(CancellationToken ct)
        => HandleResult(await specialtyService.GetAllAsync(includeInactive: true, ct));

    /// <summary>Create specialty</summary>
    /// <remarks>Creates a new specialty. Name must be unique.</remarks>
    [HttpPost("specialties")]
    [ProducesResponseType(typeof(SpecialtyResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<SpecialtyResponse>> CreateSpecialty(
        [FromBody] SpecialtyRequest dto, CancellationToken ct)
    {
        var result = await specialtyService.CreateAsync(dto, ct);
        return HandleResult(result, StatusCodes.Status201Created);
    }

    /// <summary>Update specialty</summary>
    /// <remarks>Updates name, description, or icon of an existing specialty.</remarks>
    [HttpPut("specialties/{id}")]
    [ProducesResponseType(typeof(SpecialtyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SpecialtyResponse>> UpdateSpecialty(
        [FromRoute] int id, [FromBody] SpecialtyRequest dto, CancellationToken ct)
        => HandleResult(await specialtyService.UpdateAsync(id, dto, ct));

    /// <summary>Delete specialty</summary>
    /// <remarks>Hard deletes only if no doctors are currently assigned.</remarks>
    [HttpDelete("specialties/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DeleteSpecialty(int id, CancellationToken ct)
        => HandleResult(await specialtyService.DeleteByIdAsync(id, ct));

    // ── Stats ─────────────────────────────────────────────────────────────────

    /// <summary>Platform overview stats</summary>
    /// <remarks>
    /// Returns total counts for doctors, patients, appointments,
    /// specialties, pending approvals, and reviews.
    /// </remarks>
    [HttpGet("stats/overview")]
    [ProducesResponseType(typeof(OverviewStatsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OverviewStatsResponse>> GetOverviewStats(CancellationToken ct)
        => HandleResult(await adminService.GetOverviewStatsAsync(ct));

    /// <summary>Appointment stats</summary>
    /// <remarks>Returns appointment count broken down by status.</remarks>
    [HttpGet("stats/appointments")]
    [ProducesResponseType(typeof(AppointmentStatsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AppointmentStatsResponse>> GetAppointmentStats(CancellationToken ct)
        => HandleResult(await adminService.GetAppointmentStatsAsync(ct));
}