using Domain.ResultPattern;

namespace Shared.Errors
{
    // Branches and their rooms (FR-ORG-02, 04; API_CONTRACT.md §13.25)
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
    }
}
