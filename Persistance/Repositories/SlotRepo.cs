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

        public async Task AddRangeAsync(IEnumerable<AppointmentSlot> slots, CancellationToken ct)
            => await context.AppointmentSlots.AddRangeAsync(slots, ct);

        public async Task DeleteFutureAvailableByScheduleAsync(int scheduleId, DateOnly today, CancellationToken ct)
        {
            // A cancelled appointment still points to its (now available) slot; the appointment→slot FK
            // is NoAction, so those slots must stay (REVIEW.md H4)
            await context.AppointmentSlots
                .Where(s => s.DoctorScheduleId == scheduleId
                         && s.Status == SlotStatus.Available
                         && s.Date >= today
                         && !context.Appointments.Any(a => a.SlotId == s.Id))
                .ExecuteDeleteAsync(ct);
        }

        public async Task SyncKeptSlotsWithScheduleAsync(DoctorSchedule schedule, DateOnly today, CancellationToken ct)
        {
            var kept = context.AppointmentSlots
                .Where(s => s.DoctorScheduleId == schedule.Id && s.Date >= today && s.Status != SlotStatus.Booked);

            // Same rule as DoctorSchedule.Covers, in SQL
            if (!schedule.IsActive)
            {
                await kept.Where(s => s.Status == SlotStatus.Available)
                    .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SlotStatus.Blocked), ct);
                return;
            }

            await kept.Where(s => s.Status == SlotStatus.Available
                               && (s.StartTime < schedule.StartTime || s.EndTime > schedule.EndTime))
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SlotStatus.Blocked), ct);

            await kept.Where(s => s.Status == SlotStatus.Blocked
                               && s.StartTime >= schedule.StartTime && s.EndTime <= schedule.EndTime)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SlotStatus.Available), ct);
        }

        public async Task DeleteUnusedByScheduleAsync(int scheduleId, CancellationToken ct)
            => await context.AppointmentSlots
                .Where(s => s.DoctorScheduleId == scheduleId && !context.Appointments.Any(a => a.SlotId == s.Id))
                .ExecuteDeleteAsync(ct);

        public async Task<int> WithdrawFreeSlotsAsync(int? branchId, IReadOnlyCollection<DateOnly> dates, CancellationToken ct)
        {
            var free = context.AppointmentSlots
                .Where(s => s.Status == SlotStatus.Available && dates.Contains(s.Date) && (branchId == null || s.BranchId == branchId));

            // A cancelled appointment still points to its slot (the FK is NoAction), so that one is blocked, not deleted
            var deleted = await free.Where(s => !context.Appointments.Any(a => a.SlotId == s.Id)).ExecuteDeleteAsync(ct);
            var blocked = await free.ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SlotStatus.Blocked), ct);
            return deleted + blocked;
        }

        public async Task<bool> HasUpcomingBookingsAsync(int scheduleId, DateOnly today, CancellationToken ct)
            => await context.AppointmentSlots
                .AnyAsync(s => s.DoctorScheduleId == scheduleId && s.Status == SlotStatus.Booked && s.Date >= today, ct);

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
        public async Task<int> SetStatusFromAvailableToBookedAsync(int slotId, CancellationToken ct)
        {
            var rawAffected = await context.AppointmentSlots
                .Where(s => s.Id == slotId && s.Status == SlotStatus.Available)
                .ExecuteUpdateAsync(s => s.SetProperty(slot => slot.Status, SlotStatus.Booked), ct);
            return rawAffected;
        }
    }
}
