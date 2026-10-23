using Domain.ResultPattern;

namespace Shared.Errors
{
    public class DoctorErrors
    {
        public static Error NotFound(int id) =>
         Error.NotFound("Doctor.NotFound", $"Doctor with id {id} was not found");

        public static readonly Error ScheduleDayAlreadyExists =
            Error.Conflict("Doctor.ScheduleDayAlreadyExists", "A schedule for this day of the week already exists");

        public static Error ScheduleNotFound(int id) =>
            Error.NotFound("Doctor.ScheduleNotFound", $"Schedule with id {id} was not found");

        public static readonly Error CannotDeleteScheduleWithBookedSlots =
            Error.Conflict("Doctor.CannotDeleteScheduleWithBookedSlots", "Cannot delete a schedule that has booked appointments");

        public static readonly Error InvalidScheduleTime =
            Error.Validation("Doctor.InvalidScheduleTime", "End time must be after start time");

        public static Error ProfileNotFound(Guid userId) =>
            Error.NotFound("Doctor.ProfileNotFound", $"Doctor profile not found for user {userId}");
    }
}
