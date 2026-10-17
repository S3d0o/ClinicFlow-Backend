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
            .AsNoTracking()
            .Where(s => s.DoctorProfileId == doctorProfileId
                 && s.Date == date
                 && s.Status == SlotStatus.Available
                 // Patients only see slots of approved doctors with active accounts (REVIEW.md M1)
                 && s.DoctorProfile.IsApprovedByAdmin
                 && s.DoctorProfile.User.IsActive)
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

            // A cancelled appointment still points to its (now available) slot; the appointment→slot FK
            // is NoAction, so those slots must stay (REVIEW.md H4)
            await context.AppointmentSlots
                .Where(s => s.DoctorScheduleId == scheduleId
                         && s.Status == SlotStatus.Available
                         && s.Date >= today
                         && !context.Appointments.Any(a => a.SlotId == s.Id))
                .ExecuteDeleteAsync(ct);
        }

        public async Task DeleteUnusedByScheduleAsync(int scheduleId, CancellationToken ct)
            => await context.AppointmentSlots
                .Where(s => s.DoctorScheduleId == scheduleId && !context.Appointments.Any(a => a.SlotId == s.Id))
                .ExecuteDeleteAsync(ct);

        public async Task<bool> HasUpcomingBookingsAsync(int scheduleId, CancellationToken ct)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            return await context.AppointmentSlots
                .AnyAsync(s => s.DoctorScheduleId == scheduleId && s.Status == SlotStatus.Booked && s.Date >= today, ct);
        }

        public async Task<bool> AnyForScheduleAsync(int scheduleId, CancellationToken ct)
            => await context.AppointmentSlots.AnyAsync(s => s.DoctorScheduleId == scheduleId, ct);

        public async Task<List<(DateOnly Date, TimeOnly StartTime, TimeOnly EndTime)>> GetSlotTimesAsync(
            int doctorProfileId, DateOnly from, DateOnly to, CancellationToken ct)
        {
            var slots = await context.AppointmentSlots
                .AsNoTracking()
                .Where(s => s.DoctorProfileId == doctorProfileId && s.Date >= from && s.Date <= to)
                .Select(s => new { s.Date, s.StartTime, s.EndTime })
                .ToListAsync(ct);
            return slots.Select(s => (s.Date, s.StartTime, s.EndTime)).ToList();
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
