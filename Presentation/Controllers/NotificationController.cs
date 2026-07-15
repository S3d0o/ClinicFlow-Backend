// Presentation/Controllers/NotificationController.cs
using Microsoft.AspNetCore.Authorization;
using Services.Abstraction.Contracts;
using Shared.DTOs.Notification;
using System.Security.Claims;
namespace Presentation.Controllers
{
    [Route("api/notifications")]
    [Authorize]
    public class NotificationController(INotificationService service) : ApiController
    {
        /// <summary>Get my notifications</summary>
        /// <remarks>
        /// Returns the authenticated user's notifications ordered by most recent first.
        /// Pass unreadOnly=true to get only unread notifications.
        /// </remarks>
        [HttpGet("my")]
        [ProducesResponseType(typeof(List<NotificationResponse>), StatusCodes.Status200OK)]
        public async Task<ActionResult<List<NotificationResponse>>> GetMyNotifications(
            [FromQuery] bool unreadOnly = false, CancellationToken ct = default)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var result = await service.GetMyNotificationsAsync(userId, unreadOnly, ct);
            return HandleResult(result);
        }

        /// <summary>Mark notification as read</summary>
        /// <remarks>
        /// Marks a single notification as read.
        /// Only the owner of the notification can mark it as read.
        /// </remarks>
        [HttpPost("{id}/read")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> MarkAsRead(int id, CancellationToken ct)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var result = await service.MarkAsReadAsync(userId, id, ct);
            return HandleResult(result);
        }

        /// <summary>Mark all notifications as read</summary>
        /// <remarks>Marks all of the authenticated user's unread notifications as read.</remarks>
        [HttpPost("read-all")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> MarkAllAsRead(CancellationToken ct)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var result = await service.MarkAllAsReadAsync(userId, ct);
            return HandleResult(result);
        }
    }
}
