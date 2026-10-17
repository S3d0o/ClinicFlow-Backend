using Shared.ResultPattern;

namespace Shared.Errors
{
    public static class AdminErrors
    {
        public static Error DoctorNotFound(int id) =>
            Error.NotFound("Admin.DoctorNotFound", $"Doctor with id {id} was not found");

        public static readonly Error DoctorAlreadyApproved =
            Error.Conflict("Admin.DoctorAlreadyApproved", "This doctor is already approved");

        public static readonly Error DoctorAlreadySuspended =
            Error.Conflict("Admin.DoctorAlreadySuspended", "This doctor is already suspended");
    }
}
