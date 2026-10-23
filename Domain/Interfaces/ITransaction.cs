namespace Domain.Interfaces
{
    // A transaction opened by IUnitOfWork.ExecuteInTransactionAsync. Returning without committing rolls it back
    public interface ITransaction
    {
        Task CommitAsync(CancellationToken ct);
    }
}
