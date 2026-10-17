using Domain.Enums;

namespace Persistence.Repositories
{
    public class AppointmentRepo(ClinicDbContext context) : IAppointmentRepo
    {
        public async Task<Appointment?> GetByIdAsync(int id, CancellationToken ct)
            => await context.Appointments
            .Include(x=>x.Slot)
            .Include(x => x.Doctor)
                .ThenInclude(d => d.User)
            .Include(x => x.Patient)
                .ThenInclude(p => p.User)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        public async Task<Appointment?> GetDetailedByIdAsync(int id, CancellationToken ct)
            => await context.Appointments
            .AsNoTracking()
            .Include(x=>x.Doctor)
                .ThenInclude(d=>d.User) // needed to get doctor name and email
            .Include(x=>x.Patient)
                .ThenInclude(p=>p.User) // needed to get patient name and email
            .Include(x=>x.Slot)
            .FirstOrDefaultAsync(x=>x.Id==id, ct);

        public async Task<(IReadOnlyList<Appointment> Appointments, int TotalCount)> GetDoctorsAppointmentsAsync
            (Guid userId, AppointmentStatus? status, AppointmentFilterParams filters, CancellationToken ct)
        {
            var query = context.Appointments
                .AsNoTracking()
                .Include(x => x.Doctor)
                    .ThenInclude(d => d.User) // needed for DoctorName in the response
                .Include(x => x.Patient)
                    .ThenInclude(p => p.User) // needed to get patient name and email
                .Include(x => x.Slot) // needed to get appointment date and time
                .Where(x => x.Doctor.UserId == userId);

            query = ApplyStatusFilter(query, status);

            var totalCount = await query.CountAsync(ct);

            var appointments = await query
                .OrderByDescending(x => x.Slot.Date)
                .ThenByDescending(x => x.Slot.StartTime)
                .ThenByDescending(x => x.Id) // unique tie-breaker keeps paging stable
                .Page(filters.PageNumber, filters.PageSize)
                .ToListAsync(ct);

            return (appointments, totalCount);
        }

        public async Task<(IReadOnlyList<Appointment> Appointments, int TotalCount)> GetPatientsAppointmentsAsync
            (Guid userId, AppointmentStatus? status, AppointmentFilterParams filters, CancellationToken ct)
        {
            var query = context.Appointments
                .AsNoTracking()
                .Include(x => x.Doctor)
                    .ThenInclude(d => d.User) // needed to get doctor name and email
                .Include(x => x.Doctor)
                    .ThenInclude(d => d.Specialty) // EF Core merges these correctly
                .Include(x => x.Patient)
                    .ThenInclude(p => p.User) // needed for PatientName in the response
                .Include(x => x.Review) // needed to get review details if exists
                .Include(x => x.Slot) // needed to get appointment date and time
                .Where(x => x.Patient.UserId == userId);

            query = ApplyStatusFilter(query, status);

            var totalCount = await query.CountAsync(ct);

            var appointments = await query
                .OrderByDescending(x => x.Slot.Date)
                    .ThenByDescending(x => x.Slot.StartTime)
                    .ThenByDescending(x => x.Id) // unique tie-breaker keeps paging stable
                .Page(filters.PageNumber, filters.PageSize)
                .ToListAsync(ct);

            return (appointments, totalCount);
        }


        public async Task<bool> HasCompletedAppointmentAsync(Guid patientUserId, Guid doctorUserId, CancellationToken ct)
            => await context.Appointments.AsNoTracking().AnyAsync(x=>
                x.Patient.UserId == patientUserId &&
                x.Doctor.UserId == doctorUserId &&
                x.Status == AppointmentStatus.Completed, ct);
            
        
        public async Task AddAsync(Appointment appointment, CancellationToken ct)
            => await context.Appointments.AddAsync(appointment, ct);

        public void Update(Appointment appointment)
            => context.Appointments.Update(appointment);

        public async Task<IReadOnlyList<Appointment>> GetAppointmentsNeedingReminderAsync(DateTime clinicNow, CancellationToken ct)
        {
            // Slot times are clinic-local, so the 24-hour window is measured on the clinic's clock (REVIEW.md M2)
            var now = clinicNow;
            var upperBound = now.AddHours(24);
            var today = DateOnly.FromDateTime(now);
            var lastDay = DateOnly.FromDateTime(upperBound);

            // Narrow by date in SQL (at most two calendar days), then apply the exact 24-hour window in memory
            var candidates = await context.Appointments
                .Include(a => a.Slot)
                    .ThenInclude(s=>s.DoctorProfile).ThenInclude(d=>d.User)
                .Include(a => a.Patient).ThenInclude(p => p.User)
                .Where(a => a.Status == AppointmentStatus.Confirmed
                         && a.ReminderSentAt == null
                         && a.Slot.Date >= today
                         && a.Slot.Date <= lastDay)
                .ToListAsync(ct);

            return candidates
                .Where(a =>
                {
                    var appointmentDateTime = a.Slot.Date.ToDateTime(a.Slot.StartTime);
                    return appointmentDateTime > now && appointmentDateTime <= upperBound;
                })
                .ToList();
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


        #region Helpers 

        private IQueryable<Appointment> ApplyStatusFilter(IQueryable<Appointment> query, AppointmentStatus? status) 
        {
            if (status.HasValue)
                query = query.Where(x=>x.Status == status.Value);

            return query;
        }

        #endregion
    }
}
