using Domain.ResultPattern;

namespace Shared.Errors
{
    // docs/v2/07_API_DESIGN.md §3 "Concurrency": an update that sends back a stale rowVersion
    public static class ConcurrencyErrors
    {
        public static readonly Error Conflict = Error.Conflict(
            "Concurrency.Conflict",
            "Someone else changed this record since you loaded it. Reload it and try again.");
    }
}
