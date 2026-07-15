using Domain.Entities.IdentityModule;
using Shared.DTOs.Notification;

public class NotificationService(
    IUnitOfWork uow,
    ILogger<NotificationService> logger,
    IMapper mapper) : INotificationService
{
    public async Task CreateAsync(CreateNotificationRequest request, CancellationToken ct = default)
    {
        var notification = mapper.Map<Notification>(request);
        await uow.Notifications.AddAsync(notification, ct);
        await uow.SaveChangesAsync(ct);
        logger.LogInformation(
            "Notification created for user {UserId} with type {Type}", request.UserId, request.Type);
    }

    public async Task CreateRangeAsync(
        IEnumerable<CreateNotificationRequest> requests, CancellationToken ct)
    {
        var notifications = mapper.Map<List<Notification>>(requests);
        await uow.Notifications.AddRangeAsync(notifications, ct);
        await uow.SaveChangesAsync(ct);
        logger.LogInformation("{Count} notifications created", notifications.Count);
    }

    public async Task<Result<List<NotificationResponse>>> GetMyNotificationsAsync(
        Guid userId, bool unreadOnly = false, CancellationToken ct = default)
    {
        var notifications = await uow.Notifications.GetByUserIdAsync(userId, unreadOnly, ct);
        var response = mapper.Map<List<NotificationResponse>>(notifications);
        logger.LogInformation(
            "Retrieved {Count} notifications for user {UserId}", response.Count, userId);
        return response;
    }

    public async Task<Result> MarkAsReadAsync(Guid userId, int notificationId, CancellationToken ct)
    {
        var notification = await uow.Notifications.GetByIdAsync(notificationId, ct);
        if (notification is null)
        {
            logger.LogWarning(
                "Notification {NotificationId} not found", notificationId);
            return NotificationErrors.NotFound(notificationId);
        }
        if (notification.UserId != userId)
        {
            logger.LogWarning(
                "User {UserId} attempted to read notification {NotificationId} belonging to another user",
                userId, notificationId);
            return NotificationErrors.Unauthorized;
        }

        await uow.Notifications.MarkAsReadAsync(notificationId, ct);
        // no SaveChangesAsync — ExecuteUpdateAsync writes directly
        logger.LogInformation(
            "Notification {NotificationId} marked as read for user {UserId}", notificationId, userId);
        return Result.Ok();
    }

    public async Task<Result> MarkAllAsReadAsync(Guid userId, CancellationToken ct)
    {
        await uow.Notifications.MarkAllAsReadAsync(userId, ct);
        // no SaveChangesAsync — ExecuteUpdateAsync writes directly
        logger.LogInformation(
            "All notifications marked as read for user {UserId}", userId);
        return Result.Ok();
    }
}