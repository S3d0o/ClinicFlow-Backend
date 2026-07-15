using Microsoft.AspNetCore.Authorization;
using Services.Abstraction.Contracts;
using Shared.DTOs.Review;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;

namespace Presentation.Controllers
{
    [Route("api/reviews")]
    public class ReviewController(IReviewService service) : ApiController
    {
        /// <summary>Submit a review</summary>
        /// <remarks>
        /// Patient submits a review for a completed appointment.
        /// Rating must be between 1 and 5.
        /// One review per appointment — duplicates are rejected.
        /// Doctor's average rating is recomputed after submission.
        /// </remarks>
        [Authorize(Roles = "Patient")]
        [HttpPost]
        [ProducesResponseType(typeof(ReviewResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        public async Task<ActionResult<ReviewResponse>> Submit(
            [FromBody] SubmitReviewRequest request, CancellationToken ct)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var result = await service.SubmitReviewAsync(userId, request, ct);
            return HandleResult(result, StatusCodes.Status201Created);
        }

        /// <summary>Get doctor reviews</summary>
        /// <remarks>
        /// Returns paginated visible reviews for a doctor.
        /// Ordered by most recent first.
        /// </remarks>
        [HttpGet("doctor/{doctorId}")]
        [ProducesResponseType(typeof(List<ReviewResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<List<ReviewResponse>>> GetDoctorReviews(
            int doctorId, [FromQuery] ReviewFilterRequest request, CancellationToken ct)
        {
            var result = await service.GetDoctorReviewsAsync(doctorId, request, ct);
            return HandleResult(result);
        }

        /// <summary>Delete a review</summary>
        /// <remarks>
        /// Patient can delete their own review.
        /// Admin can delete any review.
        /// Doctor's average rating is recomputed after deletion.
        /// </remarks>
        [Authorize]
        [HttpDelete("{id}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var role = User.FindFirstValue(ClaimTypes.Role)!;
            var result = await service.DeleteReviewAsync(id, userId, role, ct);
            return HandleResult(result);
        }
    }
}
