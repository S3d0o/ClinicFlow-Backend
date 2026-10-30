using Domain.ResultPattern;

namespace Shared.Errors
{
    // The user holds the permission, but not in a scope that covers the record (docs/v2/adr/0002; API_CONTRACT.md §13.20)
    public static class AccessErrors
    {
        public static readonly Error OutOfScope =
            Error.Forbidden("Access.OutOfScope", "This record is outside your access scope");
    }
}
