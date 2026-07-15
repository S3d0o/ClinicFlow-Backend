using ClinicFlow.Domain.Enums;
using Domain.Enums;
using Domain.Parameters;
using Services.MappingProfiles;
using Shared.DTOs.Appointment;
using Shared.DTOs.Notification;

namespace Services.Implementations
{
    public class AppointmentService(
        IUnitOfWork uow,
        IMapper mapper,
        ILogger<AppointmentService> logger,
        INotificationService notificationService,
        IEmailService emailService) : IAppointmentService
    {
        public async Task<Result<AppointmentResponse>> BookAppointmentAsync(Guid patientUserId, BookAppointmentRequest request, CancellationToken ct)
        {
            var patientProfile = await uow.Patients.GetPatientByUserIdAsync(patientUserId, ct);
            if (patientProfile == null)
            {
                logger.LogWarning("Patient not found for user ID: {UserId}", patientUserId);
                return PatientErrors.ProfileNotFound(patientUserId);
            }
            var slot = await uow.Slots.GetByIdAsync(request.SlotId, ct);

            if (slot == null)
            {
                logger.LogWarning("Appointment slot not found for slot ID: {SlotId}", request.SlotId);
                return AppointmentErrors.NotFound(request.SlotId);
            }
            if (slot.Status == SlotStatus.Blocked || slot.Status == SlotStatus.Booked)
            {
                logger.LogWarning("Appointment slot is not available for booking. Slot ID: {SlotId}, Status: {Status}", request.SlotId, slot.Status);
                return AppointmentErrors.SlotNotAvailable;
            }
            if (slot.Date < DateOnly.FromDateTime(DateTime.UtcNow)
                || (slot.Date == DateOnly.FromDateTime(DateTime.UtcNow) && slot.StartTime < TimeOnly.FromDateTime(DateTime.UtcNow)))
            {
                logger.LogWarning("Appointment slot is in the past and cannot be booked. Slot ID: {SlotId}, Date: {Date}, StartTime: {StartTime}", request.SlotId, slot.Date, slot.StartTime);
                return AppointmentErrors.SlotInPast;
            }
            using var transaction = await uow.BeginTransactionAsync();
            try
            {
                var SlotUpdateResult = await uow.Slots.SetStatusFromAvailableToBookedAsync(slot.Id, ct);
                if (SlotUpdateResult == 0)
                {
                    logger.LogWarning("Failed to update slot status to booked, concurrent modification detected. Slot ID: {SlotId}", slot.Id);
                    return AppointmentErrors.SlotAlreadyBooked;
                }
                var appointment = new Appointment
                {
                    SlotId = slot.Id,
                    Status = AppointmentStatus.Confirmed,
                    DoctorProfileId = slot.DoctorProfileId,
                    PatientProfileId = patientProfile.Id,
                    ReasonForVisit = request.ReasonForVisit,
                    BookedAt = DateTime.UtcNow
                };

                await uow.Appointments.AddAsync(appointment, ct);
                await uow.SaveChangesAsync(ct);
                await transaction.CommitAsync();

                logger.LogInformation("Appointment booked successfully for patient ID: {PatientId}, slot ID: {SlotId}, appointment ID: {AppointmentId}", patientProfile.Id, slot.Id, appointment.Id);

                try
                {

                    await notificationService.CreateRangeAsync(
                        [
                       new CreateNotificationRequest(
                         UserId: patientProfile.UserId, // need to include User nav
                         Title: "Appointment Confirmed",
                         Message: $"Your appointment with Dr. {slot.DoctorProfile.User.FirstName} {slot.DoctorProfile.User.LastName} on {slot.Date} at {slot.StartTime:HH:mm} has been booked.",
                         Type: NotificationType.AppointmentConfirmed,
                         RelatedEntityId: appointment.Id),

                       new CreateNotificationRequest(
                         UserId: slot.DoctorProfile.UserId, // need to include User nav
                         Title: "New Appointment Booked",
                         Message: $"You have a new appointment with {patientProfile.User.FirstName} {patientProfile.User.LastName} on {slot.Date} at {slot.StartTime:HH:mm}.",
                         Type: NotificationType.SystemAlert,
                         RelatedEntityId: appointment.Id)
                        ], ct);
                    

                    await emailService.SendAppointmentConfirmationAsync(
                        email: patientProfile.User.Email!,
                        patientName: $"{patientProfile.User.FirstName} {patientProfile.User.LastName}",
                        doctorName: $"Dr. {slot.DoctorProfile.User.FirstName} {slot.DoctorProfile.User.LastName}",
                        date: slot.Date,
                        time: slot.StartTime);
                }
                catch (Exception ex)
                {
                    // notification failure should not fail the booking
                    logger.LogError(ex, "Failed to send booking notification for appointment {AppointmentId}", appointment.Id);
                }

                return mapper.Map<AppointmentResponse>(appointment);

            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred while booking appointment for patient ID: {PatientId}, slot ID: {SlotId}", patientProfile.Id, request.SlotId);
                await transaction.RollbackAsync();
                return Error.Failure("An error occurred while booking the appointment.");
            }

        }

        public async Task<Result> CancelByPatientAsync(Guid patientUserId, int appointmentId, CancelAppointmentRequest request, CancellationToken ct)
        {
            var patientProfile = await uow.Patients.GetPatientByUserIdAsync(patientUserId, ct);

            if (patientProfile == null)
            {
                logger.LogWarning("Patient not found for user ID: {UserId}", patientUserId);
                return DoctorErrors.ProfileNotFound(patientUserId);
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
                return AppointmentErrors.Unauthorized;
            }
            if (appointment.Status == AppointmentStatus.Cancelled)
            {
                logger.LogWarning("Appointment ID: {AppointmentId} is already cancelled.", appointmentId);
                return AppointmentErrors.AlreadyCancelled;
            }
            if (appointment.Status == AppointmentStatus.Completed)
            {
                logger.LogWarning("Appointment ID: {AppointmentId} is already completed and cannot be cancelled.", appointmentId);
                return AppointmentErrors.AlreadyCompleted;
            }

            var slotDateTime = appointment.Slot.Date.ToDateTime(appointment.Slot.StartTime);

            if (DateTime.UtcNow > slotDateTime.AddHours(-2))
            {
                logger.LogWarning("Appointment ID: {AppointmentId} cannot be cancelled within 2 hours of the scheduled time.", appointmentId);
                return AppointmentErrors.CancellationWindowExpired;
            }

            appointment.Status = AppointmentStatus.Cancelled;
            appointment.CancelledAt = DateTime.UtcNow;
            appointment.CancellationReason = request.CancellationReason;
            appointment.CancelledBy = CancelledBy.Patient;
            appointment.Slot.Status = SlotStatus.Available;
            await uow.SaveChangesAsync(ct);

            logger.LogInformation("Appointment ID: {AppointmentId} cancelled by patient ID: {PatientId}.", appointmentId, patientProfile.Id);

            // Create AppointmentCancelled event and send notification to doctor and patient about the cancelled appointment
            await notificationService.CreateRangeAsync([
                new CreateNotificationRequest(
                    UserId:          patientUserId,
                    Title:           "Appointment Cancelled",
                    Message:         $"Your appointment on {appointment.Slot.Date} at {appointment.Slot.StartTime:HH:mm} has been cancelled.",
                    Type:            NotificationType.AppointmentCancelled,
                    RelatedEntityId: appointment.Id),
                new CreateNotificationRequest(
                    UserId:          appointment.Doctor.UserId,
                    Title:           "Appointment Cancelled",
                    Message:         $"Your appointment with {appointment.Patient.User.FirstName} {appointment.Patient.User.LastName} on {appointment.Slot.Date} on {appointment.Slot.Date} at {appointment.Slot.StartTime:HH:mm} has been cancelled.",
                    Type:            NotificationType.AppointmentCancelled,
                    RelatedEntityId: appointment.Id) ], ct);

            return Result.Ok();
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
                return AppointmentErrors.Unauthorized;
            }
            if (appointment.Status == AppointmentStatus.Cancelled)
            {
                logger.LogWarning("Appointment ID: {AppointmentId} is already cancelled.", appointmentId);
                return AppointmentErrors.AlreadyCancelled;
            }
            if (appointment.Status == AppointmentStatus.Completed)
            {
                logger.LogWarning("Appointment ID: {AppointmentId} is already completed and cannot be cancelled.", appointmentId);
                return AppointmentErrors.AlreadyCompleted;
            }

            appointment.Status = AppointmentStatus.Cancelled;
            appointment.CancelledAt = DateTime.UtcNow;
            appointment.CancellationReason = request.CancellationReason;
            appointment.CancelledBy = CancelledBy.Doctor;
            appointment.Slot.Status = SlotStatus.Available;
            await uow.SaveChangesAsync(ct);
            logger.LogInformation("Appointment ID: {AppointmentId} cancelled by doctor ID: {DoctorId}.", appointmentId, doctorProfile.Id);

            //Todo: Create AppointmentCancelled event and send notification to doctor and patient about the cancelled appointment
            await notificationService.CreateRangeAsync([
              new CreateNotificationRequest(
                    UserId:          appointment.Patient.UserId,
                    Title:           "Appointment Cancelled",
                    Message:         $"Your appointment on {appointment.Slot.Date} at {appointment.Slot.StartTime:HH:mm} has been cancelled.",
                    Type:            NotificationType.AppointmentCancelled,
                    RelatedEntityId: appointment.Id),
                new CreateNotificationRequest(
                    UserId:          doctorProfile.UserId,
                    Title:           "Appointment Cancelled",
                    Message:         $"Your appointment with {appointment.Patient.User.FirstName} {appointment.Patient.User.LastName} on {appointment.Slot.Date} on {appointment.Slot.Date} at {appointment.Slot.StartTime:HH:mm} has been cancelled.",
                    Type:            NotificationType.AppointmentCancelled,
                    RelatedEntityId: appointment.Id) ], ct);

            return Result.Ok();
        }
        public async Task<Result> CancelByAdminAsync(int appointmentId, CancelAppointmentRequest request, CancellationToken ct)
        {
            var appointment = await uow.Appointments.GetByIdAsync(appointmentId, ct);

            if (appointment == null)
            {
                logger.LogWarning("Appointment not found for ID: {AppointmentId}", appointmentId);
                return AppointmentErrors.NotFound(appointmentId);
            }
            if (appointment.Status == AppointmentStatus.Cancelled || appointment.Status == AppointmentStatus.Completed)
            {
                logger.LogWarning("Appointment ID: {AppointmentId} is already cancelled or completed.", appointmentId);
                return AppointmentErrors.AlreadyCancelled;
            }
            appointment.Status = AppointmentStatus.Cancelled;
            appointment.CancelledAt = DateTime.UtcNow;
            appointment.CancellationReason = request.CancellationReason;
            appointment.CancelledBy = CancelledBy.Admin;
            appointment.Slot.Status = SlotStatus.Available;
            await uow.SaveChangesAsync(ct);
            logger.LogInformation("Appointment ID: {AppointmentId} cancelled by admin.", appointmentId);

            // TODO: Create AppointmentCancelled event and send notification to doctor and patient about the cancelled appointment
            await notificationService.CreateRangeAsync([
                 new CreateNotificationRequest(
                       UserId:          appointment.Patient.UserId,
                       Title:           "Appointment Cancelled",
                       Message:         $"Your appointment on {appointment.Slot.Date} at {appointment.Slot.StartTime:HH:mm} has been cancelled.",
                       Type:            NotificationType.AppointmentCancelled,
                       RelatedEntityId: appointment.Id),
                   new CreateNotificationRequest(
                       UserId:          appointment.Doctor.UserId,
                       Title:           "Appointment Cancelled",
                       Message:         $"Your appointment with {appointment.Patient.User.FirstName} {appointment.Patient.User.LastName} on {appointment.Slot.Date} on {appointment.Slot.Date} at {appointment.Slot.StartTime:HH:mm} has been cancelled.",
                       Type:            NotificationType.AppointmentCancelled,
                       RelatedEntityId: appointment.Id) ], ct);


            return Result.Ok();
        }
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
                return AppointmentErrors.Unauthorized;
            }
            if (appointment.Status == AppointmentStatus.Cancelled)
            {
                logger.LogWarning("Cannot complete appointment ID: {AppointmentId} as it is cancelled.", appointmentId);
                return AppointmentErrors.CannotCompleteCancelled;
            }
            if (appointment.Status == AppointmentStatus.Completed)
            {
                logger.LogWarning("Appointment ID: {AppointmentId} is already completed.", appointmentId);
                return AppointmentErrors.AlreadyCompleted;
            }
            appointment.Status = AppointmentStatus.Completed;
            await uow.SaveChangesAsync(ct);
            logger.LogInformation("Appointment ID: {AppointmentId} marked as completed by doctor ID: {DoctorId}", appointmentId, doctorProfile.Id);

            await notificationService.CreateAsync(new CreateNotificationRequest(
                 UserId: appointment.Patient.UserId,
                 Title: "Appointment Completed",
                 Message: "Your appointment has been completed. You can now leave a review.",
                 Type: NotificationType.AppointmentCompleted,
                 RelatedEntityId: appointment.Id), ct);

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
                return AppointmentErrors.Unauthorized;
            }
            if (appointment.Status == AppointmentStatus.Cancelled || appointment.Status == AppointmentStatus.Pending)
            {
                logger.LogWarning("Cannot add notes to appointment ID: {AppointmentId} as it is either cancelled or pending.", appointmentId);
                return AppointmentErrors.DoctorNotesNotAllowed;
            }
            appointment.DoctorNotes = request.notes;
            await uow.SaveChangesAsync(ct);
            logger.LogInformation("Doctor notes added to appointment ID: {AppointmentId} by doctor ID: {DoctorId}", appointmentId, doctorProfile.Id);
            return Result.Ok();
        }


        public async Task<Result<AppointmentResponse>> GetAppointmentAsync(int appointmentId, CancellationToken ct)
        {
            var appointment = await uow.Appointments.GetByIdAsync(appointmentId, ct);
            if (appointment == null)
            {
                logger.LogWarning("Appointment not found for ID: {AppointmentId}", appointmentId);
                return AppointmentErrors.NotFound(appointmentId);
            }
            return mapper.Map<AppointmentResponse>(appointment);
        }

        public async Task<Result<List<AppointmentResponse>>> GetDoctorAppointmentsAsync(Guid doctorUserId, AppointmentFilterRequest request, CancellationToken ct)
        {
            var doctorProfile = await uow.Doctors.GetByUserIdAsync(doctorUserId, ct);
            if (doctorProfile == null)
            {
                logger.LogWarning("Doctor profile not found for user ID: {UserId}", doctorUserId);
                return DoctorErrors.ProfileNotFound(doctorUserId);
            }
            var filterParams = mapper.Map<AppointmentFilterParams>(request);
            var (appointments, totalCount) = await uow.Appointments.GetDoctorsAppointmentsAsync(doctorUserId, request.Status, filterParams, ct);
            return mapper.Map<List<AppointmentResponse>>(appointments);
        }

        public async Task<Result<List<AppointmentResponse>>> GetPatientAppointmentsAsync(Guid patientUserId, AppointmentFilterRequest request, CancellationToken ct)
        {
            var patientProfile = await uow.Patients.GetPatientByUserIdAsync(patientUserId, ct);
            if (patientProfile == null)
            {
                logger.LogWarning("Patient profile not found for user ID: {UserId}", patientUserId);
                return PatientErrors.ProfileNotFound(patientUserId);
            }
            var filterParams = mapper.Map<AppointmentFilterParams>(request);
            var (appointments,totalCount) = await uow.Appointments.GetPatientsAppointmentsAsync(patientUserId, request.Status, filterParams, ct);
            return mapper.Map<List<AppointmentResponse>>(appointments);
        }

        #region Helpers




        #endregion

    }
}
