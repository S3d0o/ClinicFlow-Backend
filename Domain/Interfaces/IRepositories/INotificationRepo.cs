using Domain.Entities.IdentityModule;

namespace Domain.Interfaces.IRepositories
{
    public interface INotificationRepo
    {
        // Queries
        public  Task<IReadOnlyList<Notification>> GetByUserIdAsync(Guid userId, bool unreadOnly = false, CancellationToken ct = default);
        Task<Notification?> GetByIdAsync(int id, CancellationToken ct);

        // Write
        Task AddAsync(Notification notification, CancellationToken ct);
        Task AddRangeAsync(IEnumerable<Notification> notifications, CancellationToken ct);
        public Task MarkAllAsReadAsync(Guid userId, CancellationToken ct);
        public Task MarkAsReadAsync(int notificationId, CancellationToken ct);
    }
}
