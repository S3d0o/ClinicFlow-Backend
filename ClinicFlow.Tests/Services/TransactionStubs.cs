using Domain.Interfaces;
using NSubstitute;

namespace ClinicFlow.Tests.Services
{
    // A substituted IUnitOfWork doesn't run the operation passed to ExecuteInTransactionAsync. This makes it run it
    // once with the given transaction, as the real one does when nothing needs a retry. NSubstitute can't stub an
    // open generic method, so each T the service under test uses is stubbed separately
    internal static class TransactionStubs
    {
        public static void RunsTransactionsWith<T>(this IUnitOfWork uow, ITransaction transaction)
            => uow.ExecuteInTransactionAsync(Arg.Any<Func<ITransaction, Task<T>>>(), Arg.Any<CancellationToken>())
                .Returns(call => call.ArgAt<Func<ITransaction, Task<T>>>(0)!(transaction));
    }
}
