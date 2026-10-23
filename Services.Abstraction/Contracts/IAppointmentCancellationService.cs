using Shared.DTOs.Appointment;

namespace Services.Abstraction.Contracts
{
    // One entry point per role: each finds and checks its own caller, then all share Appointment.Cancel
    public interface IAppointmentCancellationService
    {
        Task<Result> CancelByPatientAsync(
            Guid patientUserId, int appointmentId, CancelAppointmentRequest request, CancellationToken ct);
        Task<Result> CancelByDoctorAsync(
            Guid doctorUserId, int appointmentId, CancelAppointmentRequest request, CancellationToken ct);
        Task<Result> CancelByAdminAsync(
            int appointmentId, CancelAppointmentRequest request, CancellationToken ct);
    }
}
