using ClinicFlow.Domain.Enums;
using Domain.Enums;
using Shared.DTOs.Appointment;

namespace Services.Implementations
{
    // Each role finds and checks its caller; the status rules and the patient's window are Appointment.Cancel's, with the
    // window from the organization's settings (cancellationWindowHours, FR-ORG-05)
    public class AppointmentCancellationService(
        IUnitOfWork uow,
        ILogger<AppointmentCancellationService> logger,
        IClock clock,
        IOrganizationSettings settings) : IAppointmentCancellationService
    {
        public async Task<Result> CancelByPatientAsync(Guid patientUserId, int appointmentId, CancelAppointmentRequest request, CancellationToken ct)
        {
            var patientProfile = await uow.Patients.GetPatientByUserIdAsync(patientUserId, ct);

            if (patientProfile == null)
            {
                logger.LogWarning("Patient not found for user ID: {UserId}", patientUserId);
                return PatientErrors.ProfileNotFound(patientUserId);
            }
            var appointment = await uow.Appointments.GetByIdAsync(appointmentId, ct);
            if (appointment == null)
            {
                logger.LogWarning("Appointment not found for ID: {AppointmentId}", appointmentId);
                return AppointmentErrors.NotFound(appointmentId);
            }
            if (appointment.PatientProfileId != patientProfile.Id)
            {
                logger.LogWarning("Unauthorized cancellation attempt by patient ID: {PatientId} for appointment ID: {AppointmentId}", patientProfile.Id, appointmentId);
                return AppointmentErrors.Forbidden;
            }

            return await CancelAsync(appointment, CancelledBy.Patient, request.CancellationReason, ct);
        }

        public async Task<Result> CancelByDoctorAsync(Guid doctorUserId, int appointmentId, CancelAppointmentRequest request, CancellationToken ct)
        {
            var doctorProfile = await uow.Doctors.GetByUserIdAsync(doctorUserId, ct);
            if (doctorProfile == null)
            {
                logger.LogWarning("Doctor not found for user ID: {UserId}", doctorUserId);
                return DoctorErrors.ProfileNotFound(doctorUserId);
            }
            var appointment = await uow.Appointments.GetByIdAsync(appointmentId, ct);
            if (appointment == null)
            {
                logger.LogWarning("Appointment not found for ID: {AppointmentId}", appointmentId);
                return AppointmentErrors.NotFound(appointmentId);
            }
            if (appointment.DoctorProfileId != doctorProfile.Id)
            {
                logger.LogWarning("Unauthorized cancellation attempt by doctor ID: {DoctorId} for appointment ID: {AppointmentId}", doctorProfile.Id, appointmentId);
                return AppointmentErrors.Forbidden;
            }

            return await CancelAsync(appointment, CancelledBy.Doctor, request.CancellationReason, ct);
        }

        public async Task<Result> CancelByAdminAsync(int appointmentId, CancelAppointmentRequest request, CancellationToken ct)
        {
            var appointment = await uow.Appointments.GetByIdAsync(appointmentId, ct);

            if (appointment == null)
            {
                logger.LogWarning("Appointment not found for ID: {AppointmentId}", appointmentId);
                return AppointmentErrors.NotFound(appointmentId);
            }

            return await CancelAsync(appointment, CancelledBy.Admin, request.CancellationReason, ct);
        }

        // Cancels the appointment and frees its slot (or blocks it, see FreedSlotStatus). The AppointmentCancelled event
        // the entity raises is saved with the change; its handler notifies the patient and the doctor
        private async Task<Result> CancelAsync(Appointment appointment, CancelledBy cancelledBy, string? reason, CancellationToken ct)
        {
            var cancelled = appointment.Cancel(cancelledBy, clock.UtcNow, reason, (await settings.GetAsync(ct)).CancellationWindow);
            if (cancelled.IsFailure)
            {
                logger.LogWarning("Appointment {AppointmentId} ({Status}) cannot be cancelled by {CancelledBy}: {ErrorCode}",
                    appointment.Id, appointment.Status, cancelledBy, cancelled.Errors[0].Code);
                return cancelled;
            }
            appointment.Slot.Status = FreedSlotStatus(appointment.Slot);
            await uow.SaveChangesAsync(ct);
            logger.LogInformation("Appointment {AppointmentId} cancelled by {CancelledBy}", appointment.Id, cancelledBy);

            return Result.Ok();
        }

        // A cancelled appointment frees its slot, unless the doctor has since moved their hours away
        // from it or deactivated the schedule (REVIEW.md H4)
        private static SlotStatus FreedSlotStatus(AppointmentSlot slot)
            => slot.DoctorSchedule.Covers(slot) ? SlotStatus.Available : SlotStatus.Blocked;
    }
}
