namespace Domain.Interfaces.IRepositories
{
    public interface IRoomRepo
    {
        // Tracked, for BranchService's changes
        Task<Room?> GetByIdAsync(int roomId, CancellationToken ct);
        // Whether another room at the branch has either name (case-insensitive under the database collation)
        Task<bool> NameExistsAsync(int branchId, string nameEn, string nameAr, int? exceptRoomId, CancellationToken ct);
        void Add(Room room);
    }
}
