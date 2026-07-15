using ClinicFlow.Domain.Enums;

namespace Persistence.Repositories
{
    public class SlotRepo(ClinicDbContext context) : ISlotRepo
    {
        public async Task<AppointmentSlot?> GetByIdAsync(int id, CancellationToken ct)
            => await context.AppointmentSlots
            .Include(x=>x.DoctorProfile)
                .ThenInclude(x=>x.User)
            .FirstOrDefaultAsync(s=>s.Id == id, ct);

        public async Task<IReadOnlyList<AppointmentSlot>> GetAvailableByDoctorAndDateAsync(int doctorProfileId, DateOnly date, CancellationToken ct)
            => await context.AppointmentSlots
            .Where(s => s.DoctorProfileId == doctorProfileId
                 && s.Date == date
                 && s.Status == SlotStatus.Available)
            .OrderBy(s => s.StartTime)  
            .ToListAsync(ct);

        public async Task<bool> IsAvailableAsync(int slotId, CancellationToken ct)
            => await context.AppointmentSlots
            .AsNoTracking()
            .AnyAsync(s => s.Id == slotId && s.Status == SlotStatus.Available, ct);

        public void Block(AppointmentSlot slot)
        {
            slot.Status = SlotStatus.Blocked;
            context.Entry(slot).Property(s => s.Status).IsModified = true;
        }

        public async Task AddRangeAsync(IEnumerable<AppointmentSlot> slots, CancellationToken ct)
            => await context.AppointmentSlots.AddRangeAsync(slots, ct);

        public async Task DeleteFutureAvailableByScheduleAsync(int scheduleId, CancellationToken ct)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            await context.AppointmentSlots
                .Where(s => s.DoctorScheduleId == scheduleId
                         && s.Status == SlotStatus.Available
                         && s.Date >= today)
                .ExecuteDeleteAsync(ct);
        }
        public void Update(AppointmentSlot slot)
            => context.AppointmentSlots.Update(slot);

        public async Task<int> SetStatusFromAvailableToBookedAsync(int slotId, CancellationToken ct)
        {
            var rawAffected = await context.AppointmentSlots
                .Where(s => s.Id == slotId && s.Status == SlotStatus.Available)
                .ExecuteUpdateAsync(s => s.SetProperty(slot => slot.Status, SlotStatus.Booked), ct);
            return rawAffected;
        }
    }
}
