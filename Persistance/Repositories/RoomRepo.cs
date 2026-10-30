namespace Persistence.Repositories
{
    public class RoomRepo(ClinicDbContext context) : IRoomRepo
    {
        public Task<Room?> GetByIdAsync(int roomId, CancellationToken ct)
            => context.Rooms.FirstOrDefaultAsync(r => r.Id == roomId, ct);

        public Task<bool> NameExistsAsync(int branchId, string nameEn, string nameAr, int? exceptRoomId, CancellationToken ct)
            => context.Rooms.AnyAsync(r => r.BranchId == branchId && r.Id != exceptRoomId
                && (r.NameEn == nameEn || r.NameAr == nameAr), ct);

        public void Add(Room room) => context.Rooms.Add(room);
    }
}
