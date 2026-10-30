using Domain.Interfaces;
using Domain.ResultPattern;
using Shared.Errors;

namespace Shared.Concurrency
{
    // rowVersion on the wire (docs/v2/07_API_DESIGN.md §3 "Concurrency"): an entity that can be edited concurrently sends
    // its SQL Server rowversion as an opaque base64 string, and an update sends it back. A command service compares it with
    // the loaded entity's RowVersion (Check), then saves with uow.SaveOrConflictAsync, which catches a change made between
    // the load and the save; both cases return ConcurrencyErrors.Conflict (409):
    //   var check = RowVersions.Check(branch.RowVersion, request.RowVersion);
    //   if (check.IsFailure) return check;
    //   branch.Rename(...);
    //   return await uow.SaveOrConflictAsync(ct);
    public static class RowVersions
    {
        // Saves; Concurrency.Conflict, with nothing saved, when a RowVersion changed since the entities were loaded
        public static async Task<Result> SaveOrConflictAsync(this IUnitOfWork uow, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(uow);
            return await uow.TrySaveChangesAsync(ct) ? Result.Ok() : ConcurrencyErrors.Conflict;
        }

        // A SQL Server rowversion is 8 bytes
        public const int Length = 8;

        public static string Encode(byte[] rowVersion)
        {
            ArgumentNullException.ThrowIfNull(rowVersion);
            return Convert.ToBase64String(rowVersion);
        }

        public static bool TryDecode(string? value, out byte[] rowVersion)
        {
            rowVersion = [];
            if (string.IsNullOrWhiteSpace(value))
                return false;

            // Room for one byte more, so a longer value fails on its length instead of on the buffer size
            var buffer = new byte[Length + 1];
            if (!Convert.TryFromBase64String(value, buffer, out var written) || written != Length)
                return false;

            rowVersion = buffer[..Length];
            return true;
        }

        // Ok when the client's rowVersion is the entity's current one; Concurrency.Conflict otherwise (also when it
        // doesn't decode, though the validator rejects that first with a 400 on the field: ValidationRules.ValidRowVersion)
        public static Result Check(byte[] current, string? expected)
        {
            ArgumentNullException.ThrowIfNull(current);
            return TryDecode(expected, out var sent) && sent.AsSpan().SequenceEqual(current)
                ? Result.Ok()
                : ConcurrencyErrors.Conflict;
        }
    }
}
