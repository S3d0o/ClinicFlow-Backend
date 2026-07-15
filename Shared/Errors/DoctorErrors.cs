using Shared.ResultPattern;

namespace Shared.Errors
{
    public class DoctorErrors
    {
        public static Error NotFound(int id) =>
         Error.NotFound("Doctor.NotFound", $"Doctor with id {id} was not found");

        public static readonly Error NotApproved =
            Error.Forbidden("Doctor.NotApproved", "This doctor has not been approved yet");

        public static readonly Error Unauthorized =
            Error.Forbidden("Doctor.Unauthorized", "You are not authorized to perform this action on this doctor");

        public static readonly Error ScheduleDayAlreadyExists =
            Error.Failure("Doctor.ScheduleDayAlreadyExists", "A schedule for this day of the week already exists");

        public static Error ScheduleNotFound(int id) =>
            Error.NotFound("Doctor.ScheduleNotFound", $"Schedule with id {id} was not found");

        public static readonly Error CannotDeleteScheduleWithBookedSlots =
            Error.Failure("Doctor.CannotDeleteScheduleWithBookedSlots", "Cannot delete a schedule that has booked appointments");

        public static readonly Error InvalidScheduleTime =
            Error.Validation("Doctor.InvalidScheduleTime", "End time must be after start time");

        public static readonly Error InvalidConsultationFee =
            Error.Validation("Doctor.InvalidConsultationFee", "Consultation fee must be greater than 0");

        public static Error ProfileNotFound(Guid userId) =>
            Error.NotFound("Doctor.ProfileNotFound", $"Doctor profile not found for user {userId}");

       public static Error ScheduleNotFoundForDate(int doctorId, DateOnly date) =>
            Error.NotFound("Doctor.ScheduleNotFoundForDate", $"No schedule found for doctor ID {doctorId} on date {date}");
     
    }
}
