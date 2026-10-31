using Domain.Interfaces.IRepositories;
using Persistence.Repositories;

namespace Domain.Interfaces
{
    public interface IUnitOfWork
    {
        IDoctorRepo Doctors { get; }
        IAppointmentRepo Appointments { get; }
        ISlotRepo Slots { get; }
        IReviewRepo Reviews { get; }
        INotificationRepo Notifications { get; }
        ISpecialtyRepo Specialties { get; }
        IRefreshTokenRepo RefreshTokens { get; }
        IPatientRepo Patients { get; }
        IAdminRepo Admins { get; }
        IBranchRepo Branches { get; }
        IRoomRepo Rooms { get; }
        IWorkingHoursRepo WorkingHours { get; }
        IHolidayRepo Holidays { get; }
        IOrganizationSettingRepo OrganizationSettings { get; }
        IStaffRepo Staff { get; }
        IOrganizationRepo Organizations { get; }
        IMessageDeliveryRepo MessageDeliveries { get; }
        ISubscriptionRepo Subscriptions { get; }

        // Events no entity raises; entity events are added to the outbox when the entity is saved (docs/v2/06 §5)
        IOutbox Outbox { get; }

        // Runs the operation in one transaction, inside the SQL retry strategy: a transient error re-runs it, with the
        // tracked entities rewound to how they were before it started. Commit as the operation's last step; returning
        // without committing rolls back. Emails and notifications go through the outbox, so a retry never repeats them
        Task<T> ExecuteInTransactionAsync<T>(Func<ITransaction, Task<T>> operation, CancellationToken ct);

        Task<int> SaveChangesAsync(CancellationToken ct = default);

        // False, and nothing saved, when a concurrency token (RowVersion) changed since the entity was loaded
        Task<bool> TrySaveChangesAsync(CancellationToken ct);
    }
}
