using Shared.ResultPattern;

namespace Shared.Errors
{
    public static class NotificationErrors
    {
        public static Error NotFound(int id) =>
            Error.NotFound("Notification.NotFound", $"Notification with id {id} was not found");

        // 403 (not the owner). The code keeps its original "Unauthorized" name: it is part of the API contract
        public static readonly Error Forbidden =
            Error.Forbidden("Notification.Unauthorized",
                "You are not authorized to access this notification");
    }
}
