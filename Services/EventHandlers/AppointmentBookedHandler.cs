using Domain.Entities.Messaging;
using Domain.Enums;
using Shared.DTOs.Notification;

namespace Services.EventHandlers
{
    // The patient and the doctor are told in the app, and the patient by email
    public class AppointmentBookedHandler(IUnitOfWork uow, IEmailService emailService, IClock clock, ILogger<AppointmentBookedHandler> logger)
        : IDomainEventHandler<AppointmentBooked>
    {
        public async Task HandleAsync(AppointmentBooked domainEvent, long outboxMessageId, CancellationToken ct)
        {
            var appointment = await uow.Appointments.GetByIdAsync(domainEvent.AppointmentId, ct);
            if (appointment is null)
            {
                logger.LogWarning("Appointment {AppointmentId} of outbox message {MessageId} no longer exists", domainEvent.AppointmentId, outboxMessageId);
                return;
            }
            var patient = appointment.Patient.User;
            var doctor = appointment.Doctor.User;
            var when = $"on {appointment.Slot.Date} at {appointment.Slot.StartTime:HH:mm}";

            await Delivery.OnceAsync(uow, clock, outboxMessageId, DeliveryChannels.InApp, () => Delivery.AddNotificationsAsync(uow, clock, ct,
                new CreateNotificationRequest(patient.Id, "Appointment Confirmed",
                    $"Your appointment with Dr. {doctor.FirstName} {doctor.LastName} {when} has been booked.",
                    NotificationType.AppointmentConfirmed, appointment.Id),
                new CreateNotificationRequest(doctor.Id, "New Appointment Booked",
                    $"You have a new appointment with {patient.FirstName} {patient.LastName} {when}.",
                    NotificationType.SystemAlert, appointment.Id)), ct);

            await Delivery.OnceAsync(uow, clock, outboxMessageId, DeliveryChannels.Email, () => emailService.SendAppointmentConfirmationAsync(
                email: patient.Email!,
                patientName: $"{patient.FirstName} {patient.LastName}",
                doctorName: $"{doctor.FirstName} {doctor.LastName}", // the email template adds "Dr."
                date: appointment.Slot.Date,
                time: appointment.Slot.StartTime,
                ct: ct), ct);
        }
    }
}
