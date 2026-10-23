using Shared.DTOs.Appointment;

namespace Services.Abstraction.Contracts
{
    // What staff record about the visit itself
    public interface IAppointmentVisitService
    {
        // Doctor-only actions
        Task<Result> CompleteAppointmentAsync(Guid doctorUserId, int appointmentId, CancellationToken ct);
        Task<Result> AddDoctorNotesAsync(Guid doctorUserId, int appointmentId, DoctorNotesRequest request, CancellationToken ct);

        // The owning doctor or an admin marks a missed visit, once the grace period after its start has passed
        Task<Result> MarkNoShowAsync(Guid userId, string role, int appointmentId, CancellationToken ct);
    }
}
