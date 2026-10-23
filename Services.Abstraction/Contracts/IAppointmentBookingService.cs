using Shared.DTOs.Appointment;

namespace Services.Abstraction.Contracts
{
    public interface IAppointmentBookingService
    {
        // Patient books a slot — resolves PatientProfileId internally from patientUserId
        Task<Result<AppointmentResponse>> BookAppointmentAsync(
            Guid patientUserId, BookAppointmentRequest request, CancellationToken ct);
    }
}
