using Domain.Interfaces;
using Persistence.Transactions;
using Persistence.Repositories;

namespace Persistence
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly ClinicDbContext _context;

        public UnitOfWork(ClinicDbContext context)
        {
            _context = context;
        }

        private IDoctorRepo? _doctors;
        private IAppointmentRepo? _appointments;
        private ISlotRepo? _slots;
        private IReviewRepo? _reviews;
        private INotificationRepo? _notifications;
        private ISpecialtyRepo? _specialties;
        private IRefreshTokenRepo? _refreshTokens;
        private IPatientRepo? _patients;
        private IAdminRepo? _admins;
        private IBranchRepo? _branches;
        private IRoomRepo? _rooms;
        private IWorkingHoursRepo? _workingHours;
        private IHolidayRepo? _holidays;
        private IOrganizationRepo? _organizations;
        private IMessageDeliveryRepo? _messageDeliveries;
        private ISubscriptionRepo? _subscriptions;
        private IOutbox? _outbox;

        public IDoctorRepo Doctors
            => _doctors ??= new DoctorRepo(_context);

        public IAppointmentRepo Appointments
            => _appointments ??= new AppointmentRepo(_context);

        public ISlotRepo Slots
            => _slots ??= new SlotRepo(_context);

        public IReviewRepo Reviews
            => _reviews ??= new ReviewRepo(_context);

        public INotificationRepo Notifications
            => _notifications ??= new NotificationRepo(_context);

        public ISpecialtyRepo Specialties => _specialties ??= new SpecialtyRepo(_context);

        public IRefreshTokenRepo RefreshTokens => _refreshTokens ??= new RefreshTokenRepo(_context);

        public IPatientRepo Patients => _patients ??= new PatientRepo(_context);

        public IAdminRepo Admins => _admins ??= new AdminRepo(_context);
        public IBranchRepo Branches => _branches ??= new BranchRepo(_context);

        public IRoomRepo Rooms => _rooms ??= new RoomRepo(_context);
        public IWorkingHoursRepo WorkingHours => _workingHours ??= new WorkingHoursRepo(_context);
        public IHolidayRepo Holidays => _holidays ??= new HolidayRepo(_context);

        public IOrganizationRepo Organizations => _organizations ??= new OrganizationRepo(_context);

        public IMessageDeliveryRepo MessageDeliveries => _messageDeliveries ??= new MessageDeliveryRepo(_context);

        public ISubscriptionRepo Subscriptions => _subscriptions ??= new SubscriptionRepo(_context);

        public IOutbox Outbox => _outbox ??= new Outbox.Outbox(_context);

        public Task<T> ExecuteInTransactionAsync<T>(Func<ITransaction, Task<T>> operation, CancellationToken ct)
            => _context.ExecuteInTransactionAsync(operation, ct);

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
            => _context.SaveChangesAsync(ct);

        public async Task<bool> TrySaveChangesAsync(CancellationToken ct)
        {
            try
            {
                await _context.SaveChangesAsync(ct);
                return true;
            }
            catch (DbUpdateConcurrencyException)
            {
                return false;
            }
        }

    }
}
