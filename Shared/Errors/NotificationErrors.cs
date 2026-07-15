using Shared.ResultPattern;

namespace Shared.Errors
{
    public static class NotificationErrors
    {
        public static Error NotFound(int id) =>
            Error.NotFound("Notification.NotFound", $"Notification with id {id} was not found");

        public static readonly Error Unauthorized =
            Error.Forbidden("Notification.Unauthorized",
                "You are not authorized to access this notification");
    }
}
