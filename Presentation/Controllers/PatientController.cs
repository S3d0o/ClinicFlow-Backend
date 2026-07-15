using Microsoft.AspNetCore.Authorization;
using Services.Abstraction.Contracts;
using System.Security.Claims;

namespace Presentation.Controllers;

[Route("api/patients")]
public class PatientController(IPatientService service) : ApiController
{
    /// <summary>Get patient profile</summary>
    /// <remarks>Returns the authenticated patient's own profile, including medical info.</remarks>
    [Authorize(Roles = "Patient")]
    [HttpGet("profile")]
    [ProducesResponseType(typeof(PatientProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PatientProfileResponse>> GetProfile(CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await service.GetProfileAsync(userId, ct);
        return HandleResult(result);
    }

    /// <summary>Update patient profile</summary>
    /// <remarks>
    /// Partially updates the authenticated patient's profile.
    /// Only provided fields are changed — omitted fields remain unchanged.
    /// </remarks>
    [Authorize(Roles = "Patient")]
    [HttpPatch("profile")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateProfile(
        [FromBody] UpdatePatientProfileRequest request, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await service.UpdateProfileAsync(userId, request, ct);
        return HandleResult(result);
    }
}