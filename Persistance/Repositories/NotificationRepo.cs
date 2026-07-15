using Domain.Enums;

namespace Persistence.Repositories
{
    public class NotificationRepo(ClinicDbContext context) : INotificationRepo
    {
        public async Task<IReadOnlyList<Notification>> GetByUserIdAsync(
             Guid userId, bool unreadOnly = false, CancellationToken ct = default)
                 => await context.Notifications
                       .AsNoTracking()
                       .Where(n => n.UserId == userId && (!unreadOnly || !n.IsRead))
                       .OrderByDescending(n => n.CreatedAt)
                       .ToListAsync(ct);

        public async Task<Notification?> GetByIdAsync(int id, CancellationToken ct)
            => await context.Notifications
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == id, ct);       

        public async Task AddAsync(Notification notification, CancellationToken ct)
            => await context.Notifications.AddAsync(notification, ct);

        public async Task AddRangeAsync(IEnumerable<Notification> notifications, CancellationToken ct)
            => await context.Notifications.AddRangeAsync(notifications, ct);

        public async Task MarkAllAsReadAsync(Guid userId, CancellationToken ct)
            => await context.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(n => n.SetProperty(x => x.IsRead, true), ct);

        public async Task MarkAsReadAsync(int notificationId, CancellationToken ct)
            => await context.Notifications
                .Where(n => n.Id == notificationId)
                .ExecuteUpdateAsync(n => n.SetProperty(x => x.IsRead, true), ct);
    }
}
