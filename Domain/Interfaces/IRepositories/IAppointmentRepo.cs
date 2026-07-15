using Domain.Parameters;

namespace Domain.Interfaces.IRepositories
{
    public interface IAppointmentRepo
    {
        // Queries
        Task<Appointment?> GetByIdAsync(int id, CancellationToken ct);
        Task<Appointment?> GetDetailedByIdAsync(int id, CancellationToken ct);
        Task<(IReadOnlyList<Appointment> Appointments, int TotalCount)> GetPatientsAppointmentsAsync(Guid UserId, AppointmentStatus? status, AppointmentFilterParams filters, CancellationToken ct);
        Task<(IReadOnlyList<Appointment> Appointments, int TotalCount)> GetDoctorsAppointmentsAsync(Guid UserId, AppointmentStatus? status, AppointmentFilterParams filters, CancellationToken ct);
        Task<bool> HasCompletedAppointmentAsync(Guid patientUserId, Guid doctorUserId, CancellationToken ct);
        Task<IReadOnlyList<Appointment>> GetAppointmentsNeedingReminderAsync(CancellationToken ct);
        Task<(int Pending, int Confirmed, int Completed, int Cancelled, int NoShow)> GetStatsAsync(CancellationToken ct);

        // Write
        Task AddAsync(Appointment appointment, CancellationToken ct);
        void Update(Appointment appointment);
    }
}
