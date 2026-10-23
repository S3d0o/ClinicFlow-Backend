using Domain.Enums;
using Shared.DTOs.Appointment;
using Shared.DTOs.Notification;

namespace Services.Implementations
{
    // Completion, no-shows and notes. The service checks who is asking; the status and time rules are Appointment's
    public class AppointmentVisitService(
        IUnitOfWork uow,
        ILogger<AppointmentVisitService> logger,
        INotificationService notificationService,
        IClock clock) : IAppointmentVisitService
    {
        public async Task<Result> CompleteAppointmentAsync(Guid doctorUserId, int appointmentId, CancellationToken ct)
        {
            var doctorProfile = await uow.Doctors.GetByUserIdAsync(doctorUserId, ct);
            if (doctorProfile == null)
            {
                logger.LogWarning("Doctor profile not found for user ID: {UserId}", doctorUserId);
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
                logger.LogWarning("Unauthorized attempt to complete appointment ID: {AppointmentId} by doctor ID: {DoctorId}", appointmentId, doctorProfile.Id);
                return AppointmentErrors.Forbidden;
            }
            var completed = appointment.Complete(clock.UtcNow);
            if (completed.IsFailure)
            {
                logger.LogWarning("Appointment ID: {AppointmentId} ({Status}) cannot be completed: {ErrorCode}", appointmentId, appointment.Status, completed.Errors[0].Code);
                return completed;
            }
            await uow.SaveChangesAsync(ct);
            logger.LogInformation("Appointment ID: {AppointmentId} marked as completed by doctor ID: {DoctorId}", appointmentId, doctorProfile.Id);

            await notificationService.TryNotifyAsync(logger, appointment.Id, [new CreateNotificationRequest(
                 UserId: appointment.Patient.UserId,
                 Title: "Appointment Completed",
                 Message: "Your appointment has been completed. You can now leave a review.",
                 Type: NotificationType.AppointmentCompleted,
                 RelatedEntityId: appointment.Id)], ct);

            return Result.Ok();
        }

        public async Task<Result> AddDoctorNotesAsync(Guid doctorUserId, int appointmentId, DoctorNotesRequest request, CancellationToken ct)
        {
            var doctorProfile = await uow.Doctors.GetByUserIdAsync(doctorUserId, ct);
            if (doctorProfile == null)
            {
                logger.LogWarning("Doctor profile not found for user ID: {UserId}", doctorUserId);
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
                logger.LogWarning("Unauthorized attempt to add notes to appointment ID: {AppointmentId} by doctor ID: {DoctorId}", appointmentId, doctorProfile.Id);
                return AppointmentErrors.Forbidden;
            }
            var added = appointment.AddDoctorNotes(request.Notes);
            if (added.IsFailure)
            {
                logger.LogWarning("Cannot add notes to appointment ID: {AppointmentId} as it is {Status}.", appointmentId, appointment.Status);
                return added;
            }
            await uow.SaveChangesAsync(ct);
            logger.LogInformation("Doctor notes added to appointment ID: {AppointmentId} by doctor ID: {DoctorId}", appointmentId, doctorProfile.Id);
            return Result.Ok();
        }

        public async Task<Result> MarkNoShowAsync(Guid userId, string role, int appointmentId, CancellationToken ct)
        {
            var appointment = await uow.Appointments.GetByIdAsync(appointmentId, ct);
            if (appointment == null)
            {
                logger.LogWarning("Appointment not found for ID: {AppointmentId}", appointmentId);
                return AppointmentErrors.NotFound(appointmentId);
            }
            if (role != Roles.Admin && appointment.Doctor.UserId != userId)
            {
                logger.LogWarning("User {UserId} ({Role}) attempted to mark appointment {AppointmentId} they do not own as a no-show", userId, role, appointmentId);
                return AppointmentErrors.Forbidden;
            }
            var marked = appointment.MarkNoShow(clock.UtcNow);
            if (marked.IsFailure)
            {
                logger.LogWarning("Appointment ID: {AppointmentId} ({Status}) cannot be marked as a no-show: {ErrorCode}", appointmentId, appointment.Status, marked.Errors[0].Code);
                return marked;
            }

            await uow.SaveChangesAsync(ct);
            logger.LogInformation("Appointment ID: {AppointmentId} marked as a no-show by user {UserId} ({Role})", appointmentId, userId, role);

            await notificationService.TryNotifyAsync(logger, appointment.Id, [new CreateNotificationRequest(
                 UserId: appointment.Patient.UserId,
                 Title: "Missed Appointment",
                 Message: $"Your appointment on {appointment.Slot.Date} at {appointment.Slot.StartTime:HH:mm} was marked as missed.",
                 Type: NotificationType.SystemAlert,
                 RelatedEntityId: appointment.Id)], ct);

            return Result.Ok();
        }
    }
}
