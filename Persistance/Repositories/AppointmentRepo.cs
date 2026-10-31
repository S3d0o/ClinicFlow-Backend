using Domain.Enums;

namespace Persistence.Repositories
{
    public class AppointmentRepo(ClinicDbContext context) : IAppointmentRepo
    {
        public async Task<Appointment?> GetByIdAsync(int id, CancellationToken ct)
            => await context.Appointments
            .Include(x => x.Slot)
                .ThenInclude(s => s.DoctorSchedule) // a freed slot is only reopened if its schedule still covers it
            .Include(x => x.Doctor)
                .ThenInclude(d => d.User)
            .Include(x => x.Patient)
                .ThenInclude(p => p.User)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        public async Task AddAsync(Appointment appointment, CancellationToken ct)
            => await context.Appointments.AddAsync(appointment, ct);

        public async Task<IReadOnlyList<Appointment>> GetAppointmentsNeedingReminderAsync(DateTime utcNow, CancellationToken ct)
        {
            // The slot's UTC start makes the 24-hour window exact in any branch zone, across daylight saving (docs/v2/adr/0003)
            var upperBound = utcNow.AddHours(24);

            return await context.Appointments
                .Where(a => a.Status == AppointmentStatus.Confirmed
                         && a.ReminderSentAt == null
                         && a.Slot.StartsAtUtc > utcNow
                         && a.Slot.StartsAtUtc <= upperBound)
                .ToListAsync(ct);
        }
        public async Task<(int Pending, int Confirmed, int Completed, int Cancelled, int NoShow)> GetStatsAsync(CancellationToken ct)
        { 
            // One round trip instead of one COUNT per status
            var counts = await context.Appointments
                .GroupBy(a => a.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Status, x => x.Count, ct);

            int CountOf(AppointmentStatus status) => counts.TryGetValue(status, out var count) ? count : 0;

            return (CountOf(AppointmentStatus.Pending), CountOf(AppointmentStatus.Confirmed), CountOf(AppointmentStatus.Completed),
                    CountOf(AppointmentStatus.Cancelled), CountOf(AppointmentStatus.NoShow));
        }

        public async Task<IReadOnlyList<(int BranchId, DateOnly Date)>> GetUpcomingVisitDaysAsync(int? branchId, DateOnly from, CancellationToken ct)
        {
            var visits = await context.Appointments
                .AsNoTracking()
                .Where(a => (a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed)
                         && a.Slot.Date >= from
                         && (branchId == null || a.BranchId == branchId))
                .Select(a => new { a.BranchId, a.Slot.Date })
                .ToListAsync(ct);
            return visits.Select(v => (v.BranchId, v.Date)).ToList();
        }
    }
}
