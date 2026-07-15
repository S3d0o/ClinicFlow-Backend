using Shared.DTOs.Appointment;
using Shared.Errors;

namespace Services.Abstraction.Contracts
{
    public interface IAppointmentService
    {
        // Patient books a slot — resolves PatientProfileId internally from patientUserId
        Task<Result<AppointmentResponse>> BookAppointmentAsync(
            Guid patientUserId, BookAppointmentRequest request, CancellationToken ct);

        // Three separate cancel methods — each has its own rules
        Task<Result> CancelByPatientAsync(
            Guid patientUserId, int appointmentId, CancelAppointmentRequest request, CancellationToken ct);
        Task<Result> CancelByDoctorAsync(
            Guid doctorUserId, int appointmentId, CancelAppointmentRequest request, CancellationToken ct);
        Task<Result> CancelByAdminAsync(
            int appointmentId, CancelAppointmentRequest request, CancellationToken ct);

        // Doctor-only actions
        Task<Result> CompleteAppointmentAsync(Guid doctorUserId, int appointmentId, CancellationToken ct);
        Task<Result> AddDoctorNotesAsync(Guid doctorUserId, int appointmentId, DoctorNotesRequest request, CancellationToken ct);

        // Queries
        Task<Result<AppointmentResponse>> GetAppointmentAsync(int appointmentId, CancellationToken ct);
        Task<Result<List<AppointmentResponse>>> GetPatientAppointmentsAsync(Guid patientUserId, AppointmentFilterRequest request, CancellationToken ct);
        Task<Result<List<AppointmentResponse>>> GetDoctorAppointmentsAsync(Guid doctorUserId, AppointmentFilterRequest request, CancellationToken ct);
    }
}
