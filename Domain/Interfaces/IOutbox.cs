using Domain.Events;

namespace Domain.Interfaces
{
    // Adds an event that no entity raises (a request with no entity change, or an event about a row that has just
    // been inserted). It is saved by the next SaveChangesAsync, in the same transaction as the rest of the change
    public interface IOutbox
    {
        void Add(IDomainEvent domainEvent);
    }
}
