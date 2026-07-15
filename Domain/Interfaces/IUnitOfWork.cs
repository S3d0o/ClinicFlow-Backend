using Domain.Interfaces.IRepositories;
using Microsoft.EntityFrameworkCore.Storage;
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


        Task<IDbContextTransaction> BeginTransactionAsync();
        Task<int> SaveChangesAsync(CancellationToken ct = default);

    }
}
