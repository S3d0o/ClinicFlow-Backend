using Domain.ResultPattern;

namespace Shared.Errors
{
    // Branches, their hours, holidays and rooms (FR-ORG-02, 03, 04; API_CONTRACT.md §13.25)
    public static class BranchErrors
    {
        public static Error NotFound(int id) =>
            Error.NotFound("Branch.NotFound", $"Branch with id {id} was not found");

        public static readonly Error CodeTaken =
            Error.Conflict("Branch.CodeTaken", "Another branch already uses this code");

        // The organization must keep one branch that takes bookings
        public static readonly Error LastActive =
            Error.Conflict("Branch.LastActive", "The only active branch can't be deactivated");

        public static Error RoomNotFound(int id) =>
            Error.NotFound("Room.NotFound", $"Room with id {id} was not found");

        public static readonly Error RoomNameTaken =
            Error.Conflict("Room.NameTaken", "Another room at this branch already has this name");

        // A day's periods start after they end or overlap, or a weekday is unknown or repeated (FR-ORG-03)
        public static readonly Error InvalidHours =
            Error.Validation("Branch.InvalidHours", "Each opening period must end after it starts, and a day's periods may not overlap");

        public static Error HolidayNotFound(int id) =>
            Error.NotFound("Holiday.NotFound", $"Holiday with id {id} was not found");
    }
}
