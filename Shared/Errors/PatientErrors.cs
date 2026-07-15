using Shared.ResultPattern;

namespace Shared.Errors
{
    public class PatientErrors
    {
        public static Error ProfileNotFound(Guid userId) =>
        Error.NotFound("Patient.ProfileNotFound", $"Patient profile not found for user {userId}");
    }
}
