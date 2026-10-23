using ClinicFlow.Domain.Enums;
using Domain.Enums;
using Shared.DTOs.Appointment;
using Shared.DTOs.Notification;

namespace Services.Implementations
{
    public class AppointmentBookingService(
        IUnitOfWork uow,
        IMapper mapper,
        ILogger<AppointmentBookingService> logger,
        INotificationService notificationService,
        IEmailService emailService,
        IClock clock) : IAppointmentBookingService
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
            // Every rule compares the slot's UTC start with UtcNow, so it holds in any branch zone and across daylight saving (docs/v2/adr/0003)
            if (slot.StartsAtUtc < clock.UtcNow)
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
                    var created = Appointment.Book(slot, patientProfile.Id, request.ReasonForVisit, clock.UtcNow);

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
    }
}
