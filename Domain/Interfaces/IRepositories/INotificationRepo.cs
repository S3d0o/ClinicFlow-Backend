using Domain.Entities.IdentityModule;

namespace Domain.Interfaces.IRepositories
{
    public interface INotificationRepo
    {
        // Queries
        // Newest first, one page, plus the total matching count
        Task<(IReadOnlyList<Notification> Notifications, int TotalCount)> GetByUserIdAsync(Guid userId, bool unreadOnly, int pageNumber, int pageSize, CancellationToken ct);
        Task<Notification?> GetByIdAsync(int id, CancellationToken ct);

        // Write
        Task AddAsync(Notification notification, CancellationToken ct);
        Task AddRangeAsync(IEnumerable<Notification> notifications, CancellationToken ct);
        public Task MarkAllAsReadAsync(Guid userId, CancellationToken ct);
        public Task MarkAsReadAsync(int notificationId, CancellationToken ct);
    }
}
