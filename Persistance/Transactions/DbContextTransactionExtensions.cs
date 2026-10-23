using Domain.Interfaces;
using Microsoft.EntityFrameworkCore.Storage;

namespace Persistence.Transactions
{
    // Manual transactions under EnableRetryOnFailure (REVIEW.md NL14). EF only allows them inside the execution
    // strategy, which re-runs the whole operation after a transient error. The retry runs on the same DbContext,
    // so the change tracker is rewound first (ChangeTrackerSnapshot): otherwise entities added by the failed attempt
    // would be inserted twice, and changes it had saved (then rolled back) would be lost as "already saved".
    public static class DbContextTransactionExtensions
    {
        public static Task<T> ExecuteInTransactionAsync<T>(
            this DbContext context, Func<ITransaction, Task<T>> operation, CancellationToken ct)
        {
            var snapshot = ChangeTrackerSnapshot.Take(context);

            return context.Database.CreateExecutionStrategy().ExecuteAsync(async attemptCt =>
            {
                await using var efTransaction = await context.Database.BeginTransactionAsync(attemptCt);
                var transaction = new Transaction(efTransaction);
                try
                {
                    var result = await operation(transaction);
                    if (!transaction.IsCommitted)
                        snapshot.Restore(); // rolled back when disposed
                    return result;
                }
                catch (Exception ex) when (transaction.IsCommitted)
                {
                    // Re-running a committed operation would apply it twice, so this must not look transient
                    throw new InvalidOperationException("The transaction was committed, but the operation failed afterwards.", ex);
                }
                catch
                {
                    snapshot.Restore(); // rolled back when disposed; a transient error retries from here
                    throw;
                }
            }, ct);
        }

        public static Task ExecuteInTransactionAsync(
            this DbContext context, Func<ITransaction, Task> operation, CancellationToken ct)
            => context.ExecuteInTransactionAsync(async transaction =>
            {
                await operation(transaction);
                return true;
            }, ct);

        private sealed class Transaction(IDbContextTransaction transaction) : ITransaction
        {
            public bool IsCommitted { get; private set; }

            public async Task CommitAsync(CancellationToken ct)
            {
                await transaction.CommitAsync(ct);
                IsCommitted = true;
            }
        }
    }
}
