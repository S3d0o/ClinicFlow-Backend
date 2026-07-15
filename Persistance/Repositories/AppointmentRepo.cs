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
                .Include(x => x.Patient)
                    .ThenInclude(p => p.User) // needed to get patient name and email
                .Include(x => x.Slot) // needed to get appointment date and time
                .Where(x => x.Doctor.UserId == userId);

            query = ApplyStatusFilter(query, status);

            var totalCount = await query.CountAsync(ct);

            var pageNumber = Math.Max(1, filters.PageNumber);
            var pageSize = Math.Max(1, filters.PageSize);

            var appointments = await query
                .OrderByDescending(x => x.Slot.Date)
                .ThenByDescending(x => x.Slot.StartTime)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
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
                .Include(x => x.Review) // needed to get review details if exists
                .Include(x => x.Slot) // needed to get appointment date and time
                .Where(x => x.Patient.UserId == userId);

            query = ApplyStatusFilter(query, status);

            var totalCount = await query.CountAsync(ct);

            var appointments = await query
                .OrderByDescending(x => x.Slot.Date)
                    .ThenByDescending(x => x.Slot.StartTime)
                .Skip((filters.PageNumber - 1) * filters.PageSize)
                .Take(filters.PageSize)
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

        public async Task<IReadOnlyList<Appointment>> GetAppointmentsNeedingReminderAsync(CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var upperBound = now.AddHours(24);

            var appointments = context.Appointments
                .Include(a => a.Slot)
                    .ThenInclude(s=>s.DoctorProfile).ThenInclude(d=>d.User)
                .Include(a => a.Patient).ThenInclude(p => p.User)
                .Where(a => a.Status == AppointmentStatus.Confirmed
                         && a.ReminderSentAt == null)
                .AsEnumerable() // pull into memory for the DateTime combination
                .Where(a =>
                {
                    var appointmentDateTime = a.Slot.Date.ToDateTime(a.Slot.StartTime);
                    return appointmentDateTime > now && appointmentDateTime <= upperBound;
                })
                .ToList();
            return appointments;
        }
        public async Task<(int Pending, int Confirmed, int Completed, int Cancelled, int NoShow)> GetStatsAsync(CancellationToken ct)
        { 
            var pendingAppointments = await context.Appointments.CountAsync(a => a.Status == AppointmentStatus.Pending, ct);
            var confirmedAppointments = await context.Appointments.CountAsync(a => a.Status == AppointmentStatus.Confirmed, ct);
            var completedAppointments = await context.Appointments.CountAsync(a => a.Status == AppointmentStatus.Completed, ct);
            var cancelledAppointments = await context.Appointments.CountAsync(a => a.Status == AppointmentStatus.Cancelled, ct);
            var noShowAppointments = await context.Appointments.CountAsync(a => a.Status == AppointmentStatus.NoShow, ct);

            return (pendingAppointments, confirmedAppointments, completedAppointments, cancelledAppointments, noShowAppointments);
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
