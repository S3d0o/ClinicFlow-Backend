using Shared.ResultPattern;

namespace Shared.Errors
{
    public static class SpecialtyErrors
    {
        public static Error NotFound(int id) =>
        Error.NotFound("Specialty.NotFound", $"Specialty with id {id} was not found");

        public static readonly Error NameAlreadyExists =
            Error.Failure("Specialty.NameAlreadyExists", "A specialty with this name already exists");

        public static readonly Error HasAssignedDoctors =
            Error.Failure("Specialty.HasAssignedDoctors", "Cannot delete a specialty with assigned doctors");
    }
}
