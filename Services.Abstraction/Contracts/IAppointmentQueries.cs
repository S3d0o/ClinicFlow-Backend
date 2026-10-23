using Shared.DTOs.Appointment;

namespace Services.Abstraction.Contracts
{
    // The appointment reads (CQRS-lite, docs/v2/adr/0006): untracked projections straight to the response,
    // implemented in Persistence. They never write
    public interface IAppointmentQueries
    {
        // Only the owning patient, the owning doctor or an admin may read an appointment
        Task<Result<AppointmentResponse>> GetAppointmentAsync(int appointmentId, Guid userId, string role, CancellationToken ct);
        Task<Result<List<AppointmentResponse>>> GetPatientAppointmentsAsync(Guid patientUserId, AppointmentFilterRequest request, CancellationToken ct);
        Task<Result<List<AppointmentResponse>>> GetDoctorAppointmentsAsync(Guid doctorUserId, AppointmentFilterRequest request, CancellationToken ct);
    }
}
