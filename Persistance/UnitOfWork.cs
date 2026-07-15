using Domain.Interfaces;
using Microsoft.EntityFrameworkCore.Storage;
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

        public async Task<IDbContextTransaction> BeginTransactionAsync()
        => await _context.Database.BeginTransactionAsync();
        public Task<int> SaveChangesAsync(CancellationToken ct = default)
            => _context.SaveChangesAsync(ct);

    }
}
