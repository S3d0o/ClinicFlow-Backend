using ClinicFlow.Domain.Enums;
using Domain.Enums;
using Domain.Parameters;
using Shared.DTOs.Appointment;
using Shared.DTOs.Notification;

namespace Services.Implementations
{
    public class AppointmentService(
        IUnitOfWork uow,
        IMapper mapper,
        ILogger<AppointmentService> logger,
        INotificationService notificationService,
        IEmailService emailService,
        IClock clock) : IAppointmentService
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
                return AppointmentErrors.SlotNotFound(request.SlotId);
            }
            // Slots of unapproved or suspended doctors aren't listed, so they can't be booked either (REVIEW.md M1)
            if (!slot.DoctorProfile.IsApprovedByAdmin || !slot.DoctorProfile.User.IsActive)
            {
                logger.LogWarning("Slot {SlotId} belongs to doctor {DoctorId}, who is not approved or not active", request.SlotId, slot.DoctorProfileId);
                return AppointmentErrors.SlotNotFound(request.SlotId);
            }
            if (slot.Status == SlotStatus.Blocked || slot.Status == SlotStatus.Booked)
            {
                logger.LogWarning("Appointment slot is not available for booking. Slot ID: {SlotId}, Status: {Status}", request.SlotId, slot.Status);
                return AppointmentErrors.SlotNotAvailable;
            }
            // Slot times are clinic-local, so compare them with the clinic's wall clock (REVIEW.md M2)
            if (slot.Date.ToDateTime(slot.StartTime) < clock.ClinicNow)
            {
                logger.LogWarning("Appointment slot is in the past and cannot be booked. Slot ID: {SlotId}, Date: {Date}, StartTime: {StartTime}", request.SlotId, slot.Date, slot.StartTime);
                return AppointmentErrors.SlotInPast;
            }
            Result<Appointment> booked;
            try
            {
                booked = await uow.ExecuteInTransactionAsync<Result<Appointment>>(async transaction =>
                {
                    var bookedRows = await uow.Slots.SetStatusFromAvailableToBookedAsync(slot.Id, ct);
                    if (bookedRows == 0)
                    {
                        logger.LogWarning("Failed to update slot status to booked, concurrent modification detected. Slot ID: {SlotId}", slot.Id);
                        return AppointmentErrors.SlotAlreadyBooked;
                    }
                    var created = new Appointment
                    {
                        SlotId = slot.Id,
                        Status = AppointmentStatus.Confirmed,
                        DoctorProfileId = slot.DoctorProfileId,
                        PatientProfileId = patientProfile.Id,
                        ReasonForVisit = request.ReasonForVisit,
                        BookedAt = clock.UtcNow
                    };

                    await uow.Appointments.AddAsync(created, ct);
                    await uow.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
                    return created;
                }, ct);
            }
            catch (Exception ex)
            {
                // The transaction has been rolled back
                logger.LogError(ex, "Error occurred while booking appointment for patient ID: {PatientId}, slot ID: {SlotId}", patientProfile.Id, request.SlotId);
                return Error.Failure("An error occurred while booking the appointment.");
            }

            if (booked.IsFailure)
                return Result<AppointmentResponse>.Fail(booked.Errors);

            var appointment = booked.Value;
            logger.LogInformation("Appointment booked successfully for patient ID: {PatientId}, slot ID: {SlotId}, appointment ID: {AppointmentId}", patientProfile.Id, slot.Id, appointment.Id);

            // After the commit and outside the transaction, so a retried transaction can't send them twice
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
                    doctorName: $"{slot.DoctorProfile.User.FirstName} {slot.DoctorProfile.User.LastName}", // the email template adds "Dr."
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
            if (CannotCancelError(appointment) is { } statusError)
                return statusError;

            var slotDateTime = appointment.Slot.Date.ToDateTime(appointment.Slot.StartTime);

            if (clock.ClinicNow > slotDateTime.AddHours(-2))
            {
                logger.LogWarning("Appointment ID: {AppointmentId} cannot be cancelled within 2 hours of the scheduled time.", appointmentId);
                return AppointmentErrors.CancellationWindowExpired;
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
            if (CannotCancelError(appointment) is { } statusError)
                return statusError;

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
            if (CannotCancelError(appointment) is { } statusError)
                return statusError;

            return await CancelAsync(appointment, CancelledBy.Admin, request.CancellationReason, ct);
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
                return AppointmentErrors.Forbidden;
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
            if (appointment.Status == AppointmentStatus.NoShow)
            {
                logger.LogWarning("Appointment ID: {AppointmentId} was marked as a no-show and cannot be completed.", appointmentId);
                return AppointmentErrors.AlreadyNoShow;
            }
            // A visit can only be completed once it has started (REVIEW.md M14); completing early would unlock a review
            var slotStart = appointment.Slot.Date.ToDateTime(appointment.Slot.StartTime);
            if (clock.ClinicNow < slotStart)
            {
                logger.LogWarning("Appointment ID: {AppointmentId} cannot be completed before it starts at {SlotStart}.", appointmentId, slotStart);
                return AppointmentErrors.CannotCompleteBeforeStart;
            }
            appointment.Status = AppointmentStatus.Completed;
            await uow.SaveChangesAsync(ct);
            logger.LogInformation("Appointment ID: {AppointmentId} marked as completed by doctor ID: {DoctorId}", appointmentId, doctorProfile.Id);

            await TryNotifyAsync(appointment.Id, [new CreateNotificationRequest(
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
            if (appointment.Status == AppointmentStatus.Cancelled || appointment.Status == AppointmentStatus.Pending)
            {
                logger.LogWarning("Cannot add notes to appointment ID: {AppointmentId} as it is either cancelled or pending.", appointmentId);
                return AppointmentErrors.DoctorNotesNotAllowed;
            }
            appointment.DoctorNotes = request.Notes;
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
            if (role != "Admin" && appointment.Doctor.UserId != userId)
            {
                logger.LogWarning("User {UserId} ({Role}) attempted to mark appointment {AppointmentId} they do not own as a no-show", userId, role, appointmentId);
                return AppointmentErrors.Forbidden;
            }
            var statusError = appointment.Status switch
            {
                AppointmentStatus.Cancelled => AppointmentErrors.AlreadyCancelled,
                AppointmentStatus.Completed => AppointmentErrors.AlreadyCompleted,
                AppointmentStatus.NoShow => AppointmentErrors.AlreadyNoShow,
                _ => null
            };
            if (statusError is not null)
            {
                logger.LogWarning("Appointment ID: {AppointmentId} is {Status} and cannot be marked as a no-show.", appointmentId, appointment.Status);
                return statusError;
            }
            // The patient may just be late: wait out the grace period after the start (v2 FR-APT-07)
            var slotStart = appointment.Slot.Date.ToDateTime(appointment.Slot.StartTime);
            if (clock.ClinicNow < slotStart + NoShowGracePeriod)
            {
                logger.LogWarning("Appointment ID: {AppointmentId} cannot be marked as a no-show before {GraceEnd}.", appointmentId, slotStart + NoShowGracePeriod);
                return AppointmentErrors.NoShowTooEarly;
            }

            appointment.Status = AppointmentStatus.NoShow;
            await uow.SaveChangesAsync(ct);
            logger.LogInformation("Appointment ID: {AppointmentId} marked as a no-show by user {UserId} ({Role})", appointmentId, userId, role);

            await TryNotifyAsync(appointment.Id, [new CreateNotificationRequest(
                 UserId: appointment.Patient.UserId,
                 Title: "Missed Appointment",
                 Message: $"Your appointment on {appointment.Slot.Date} at {appointment.Slot.StartTime:HH:mm} was marked as missed.",
                 Type: NotificationType.SystemAlert,
                 RelatedEntityId: appointment.Id)], ct);

            return Result.Ok();
        }

        public async Task<Result<AppointmentResponse>> GetAppointmentAsync(int appointmentId, Guid userId, string role, CancellationToken ct)
        {
            var appointment = await uow.Appointments.GetDetailedByIdAsync(appointmentId, ct); // read-only, untracked (REVIEW.md NL1)
            if (appointment == null)
            {
                logger.LogWarning("Appointment not found for ID: {AppointmentId}", appointmentId);
                return AppointmentErrors.NotFound(appointmentId);
            }

            // Appointments contain medical data: only the owning patient, the owning doctor or an admin may read them
            var canRead = role switch
            {
                "Admin" => true,
                "Patient" => appointment.Patient.UserId == userId,
                "Doctor" => appointment.Doctor.UserId == userId,
                _ => false
            };
            if (!canRead)
            {
                logger.LogWarning("User {UserId} ({Role}) attempted to read appointment {AppointmentId} they do not own", userId, role, appointmentId);
                return AppointmentErrors.Forbidden;
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
            var (appointments, totalCount) = await uow.Appointments.GetByDoctorUserIdAsync(doctorUserId, request.Status, filterParams, ct);
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
            var (appointments,totalCount) = await uow.Appointments.GetByPatientUserIdAsync(patientUserId, request.Status, filterParams, ct);
            return mapper.Map<List<AppointmentResponse>>(appointments);
        }

        #region Helpers

        private static readonly TimeSpan NoShowGracePeriod = TimeSpan.FromMinutes(15);

        // A cancelled appointment frees its slot, unless the doctor has since moved their hours away
        // from it or deactivated the schedule (REVIEW.md H4)
        private static SlotStatus FreedSlotStatus(AppointmentSlot slot)
            => slot.DoctorSchedule.Covers(slot) ? SlotStatus.Available : SlotStatus.Blocked;

        // The status rules every cancel path shares; the caller checks ownership (and, for patients, the 2-hour window) first
        private Error? CannotCancelError(Appointment appointment)
        {
            var error = appointment.Status switch
            {
                AppointmentStatus.Cancelled => AppointmentErrors.AlreadyCancelled,
                AppointmentStatus.Completed => AppointmentErrors.AlreadyCompleted,
                AppointmentStatus.NoShow => AppointmentErrors.AlreadyNoShow,
                _ => null
            };
            if (error is not null)
                logger.LogWarning("Appointment {AppointmentId} is {Status} and cannot be cancelled", appointment.Id, appointment.Status);
            return error;
        }

        // Cancels the appointment, frees its slot (or blocks it, see FreedSlotStatus), saves, then notifies the patient and the doctor
        private async Task<Result> CancelAsync(Appointment appointment, CancelledBy cancelledBy, string? reason, CancellationToken ct)
        {
            appointment.Status = AppointmentStatus.Cancelled;
            appointment.CancelledAt = clock.UtcNow;
            appointment.CancellationReason = reason;
            appointment.CancelledBy = cancelledBy;
            appointment.Slot.Status = FreedSlotStatus(appointment.Slot);
            await uow.SaveChangesAsync(ct);
            logger.LogInformation("Appointment {AppointmentId} cancelled by {CancelledBy}", appointment.Id, cancelledBy);

            // TODO: an AppointmentCancelled domain event (V2, REVIEW.md §4.4)
            await TryNotifyAsync(appointment.Id, [
                new CreateNotificationRequest(
                    UserId:          appointment.Patient.UserId,
                    Title:           "Appointment Cancelled",
                    Message:         $"Your appointment on {appointment.Slot.Date} at {appointment.Slot.StartTime:HH:mm} has been cancelled.",
                    Type:            NotificationType.AppointmentCancelled,
                    RelatedEntityId: appointment.Id),
                new CreateNotificationRequest(
                    UserId:          appointment.Doctor.UserId,
                    Title:           "Appointment Cancelled",
                    Message:         $"Your appointment with {appointment.Patient.User.FirstName} {appointment.Patient.User.LastName} on {appointment.Slot.Date} at {appointment.Slot.StartTime:HH:mm} has been cancelled.",
                    Type:            NotificationType.AppointmentCancelled,
                    RelatedEntityId: appointment.Id) ], ct);

            return Result.Ok();
        }

        // Runs after the appointment change is committed: a notification failure is logged
        // and must not turn the successful operation into an error response
        private async Task TryNotifyAsync(int appointmentId, IEnumerable<CreateNotificationRequest> notifications, CancellationToken ct)
        {
            try
            {
                await notificationService.CreateRangeAsync(notifications, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create notifications for appointment {AppointmentId}", appointmentId);
            }
        }

        #endregion

    }
}
