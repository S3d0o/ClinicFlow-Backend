using Domain.Enums;

namespace Domain.Errors
{
    public static class AppointmentErrors
    {
        public static Error NotFound(int id) =>
            Error.NotFound("Appointment.NotFound",
                $"Appointment with id {id} was not found");

        public static Error SlotNotFound(int slotId) =>
            Error.NotFound("Appointment.SlotNotFound",
                $"Appointment slot with id {slotId} was not found");

        public static readonly Error SlotAlreadyBooked =
            Error.Conflict("Appointment.SlotAlreadyBooked",
                "This appointment slot has already been booked");

        public static readonly Error SlotNotAvailable =
            Error.Conflict("Appointment.SlotNotAvailable",
                "This appointment slot is not available");

        public static readonly Error SlotInPast =
            Error.Validation("Appointment.SlotInPast",
                "Cannot book an appointment in the past");

        public static readonly Error AlreadyCancelled =
            Error.Conflict("Appointment.AlreadyCancelled",
                "This appointment has already been cancelled");

        public static readonly Error AlreadyCompleted =
            Error.Conflict("Appointment.AlreadyCompleted",
                "This appointment has already been completed");

        public static readonly Error CancellationWindowExpired =
            Error.Validation("Appointment.CancellationWindowExpired",
                "Appointments can only be cancelled at least 2 hours before the scheduled time");

        // 403 (not the owner). The code keeps its original "Unauthorized" name: it is part of the API contract
        public static readonly Error Forbidden =
            Error.Forbidden("Appointment.Unauthorized",
                "You are not authorized to perform this action on this appointment");

        public static readonly Error CannotCompleteCancelled =
            Error.Conflict("Appointment.CannotCompleteCancelled",
                "Cancelled appointments cannot be marked as completed");

        public static readonly Error CannotCompleteBeforeStart =
            Error.Validation("Appointment.CannotCompleteBeforeStart",
                "An appointment can only be marked as completed once it has started");

        public static readonly Error AlreadyNoShow =
            Error.Conflict("Appointment.AlreadyNoShow",
                "This appointment has been marked as a no-show");

        public static readonly Error NoShowTooEarly =
            Error.Validation("Appointment.NoShowTooEarly",
                "An appointment can only be marked as a no-show 15 minutes after it starts");

        public static readonly Error DoctorNotesNotAllowed =
            Error.Conflict("Appointment.DoctorNotesNotAllowed",
                "Doctor notes cannot be added to a cancelled appointment");

        // Any move the state machine doesn't allow and no older code covers (docs/v2/adr/0004)
        public static Error InvalidStatusTransition(AppointmentStatus from, AppointmentStatus to) =>
            Error.Conflict("Appointment.InvalidStatusTransition",
                $"An appointment that is {from} cannot become {to}");

        public static readonly Error CheckInNotToday =
            Error.Validation("Appointment.CheckInNotToday",
                "Patients can only be checked in on the day of their appointment");

        public static readonly Error NoShowCorrectionExpired =
            Error.Validation("Appointment.NoShowCorrectionExpired",
                "A no-show can only be corrected on the day of the appointment");

        public static readonly Error ReviewAlreadyExists =
            Error.Conflict("Appointment.ReviewAlreadyExists",
                "A review has already been submitted for this appointment");

        public static readonly Error ReviewNotAllowed =
            Error.Validation("Appointment.ReviewNotAllowed",
                "Reviews can only be submitted for completed appointments");
    }
}