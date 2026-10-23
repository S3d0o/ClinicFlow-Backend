using Domain.Parameters;

namespace Domain.Interfaces.IRepositories
{
    public interface IAppointmentRepo
    {
        // Queries
        Task<Appointment?> GetByIdAsync(int id, CancellationToken ct);
        // Untracked, with doctor, patient and slot, for read-only responses (REVIEW.md NL1)
        Task<Appointment?> GetDetailedByIdAsync(int id, CancellationToken ct);
        Task<(IReadOnlyList<Appointment> Appointments, int TotalCount)> GetByPatientUserIdAsync(Guid patientUserId, AppointmentStatus? status, AppointmentFilterParams filters, CancellationToken ct);
        Task<(IReadOnlyList<Appointment> Appointments, int TotalCount)> GetByDoctorUserIdAsync(Guid doctorUserId, AppointmentStatus? status, AppointmentFilterParams filters, CancellationToken ct);
        Task<IReadOnlyList<Appointment>> GetAppointmentsNeedingReminderAsync(DateTime utcNow, CancellationToken ct);
        Task<(int Pending, int Confirmed, int Completed, int Cancelled, int NoShow)> GetStatsAsync(CancellationToken ct);

        // Write
        Task AddAsync(Appointment appointment, CancellationToken ct);
    }
}
