using Shared.ResultPattern;

namespace Shared.Errors
{
    public static class AppointmentErrors
    {
        public static Error NotFound(int id) =>
            Error.NotFound("Appointment.NotFound",
                $"Appointment with id {id} was not found");

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

        public static readonly Error CannotCancelCompleted =
            Error.Failure("Appointment.CannotCancelCompleted",
                "Completed appointments cannot be cancelled");

        public static readonly Error CancellationWindowExpired =
            Error.Validation("Appointment.CancellationWindowExpired",
                "Appointments can only be cancelled at least 2 hours before the scheduled time");

        public static readonly Error Unauthorized =
            Error.Forbidden("Appointment.Unauthorized",
                "You are not authorized to perform this action on this appointment");

        public static readonly Error CannotCompleteCancelled =
            Error.Failure("Appointment.CannotCompleteCancelled",
                "Cancelled appointments cannot be marked as completed");

        public static readonly Error InvalidStatusTransition =
            Error.Validation("Appointment.InvalidStatusTransition",
                "The appointment cannot transition to the requested status");

        public static readonly Error DoctorNotesNotAllowed =
            Error.Failure("Appointment.DoctorNotesNotAllowed",
                "Doctor notes cannot be added to a cancelled appointment");

        public static readonly Error ReviewAlreadyExists =
            Error.Conflict("Appointment.ReviewAlreadyExists",
                "A review has already been submitted for this appointment");

        public static readonly Error ReviewNotAllowed =
            Error.Validation("Appointment.ReviewNotAllowed",
                "Reviews can only be submitted for completed appointments");
    }
}