using Microsoft.AspNetCore.Authorization;
using Services.Abstraction.Contracts;
using Shared.DTOs.Profile;
using System.Security.Claims;

namespace Presentation.Controllers
{
    [Route("api/profile")]
    [Authorize]
    public class ProfileController(IProfileService service) : ApiController
    {
        /// <summary>Get current user profile</summary>
        /// <remarks>
        /// Returns the authenticated user's profile.
        /// Works for all roles — Patient, Doctor, Admin.
        /// For role-specific medical or professional info use the
        /// patient or doctor profile endpoints.
        /// </remarks>
        [HttpGet("me")]
        [ProducesResponseType(typeof(UserProfileResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<UserProfileResponse>> GetProfile(CancellationToken ct)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var result = await service.GetProfileAsync(userId, ct);
            return HandleResult(result);
        }

        /// <summary>Update current user profile</summary>
        /// <remarks>
        /// Partially updates the authenticated user's profile.
        /// Only provided fields are changed — omitted fields remain unchanged.
        /// To change email use the auth endpoints.
        /// </remarks>
        [HttpPatch("me")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdateProfile(
            [FromBody] UpdateUserProfileRequest request, CancellationToken ct)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var result = await service.UpdateProfileAsync(userId, request, ct);
            return HandleResult(result);
        }
    }
}
