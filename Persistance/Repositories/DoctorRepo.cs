using Domain.Enums;
using System.ComponentModel.DataAnnotations;

namespace Persistence.Repositories
{
    public class DoctorRepo(ClinicDbContext context) : IDoctorRepo
    {
        // this method hits when patient clicks on doctor profile to view details, so we need to include specialty and user info for display
        public async Task<DoctorProfile?> GetDetailedByIdAsync(int id, CancellationToken ct)
        {
            return await context.DoctorProfiles
                .Include(d => d.Specialty)
                .Include(d => d.User)
                .AsNoTracking()
                // Public profile: only approved doctors whose accounts are active (REVIEW.md M1)
                .FirstOrDefaultAsync(d => d.Id == id && d.IsApprovedByAdmin && d.User.IsActive, ct);

        }
        public async Task<DoctorProfile?> GetByIdAsync(int id, CancellationToken ct)
            => await context.DoctorProfiles.Include(s=>s.User).Include(s=>s.Specialty).FirstOrDefaultAsync(d => d.Id == id, ct);

        public async Task<DoctorProfile?> GetByUserIdAsync(Guid userId, CancellationToken ct)
            => await context.DoctorProfiles.FirstOrDefaultAsync(d => d.UserId == userId, ct);

        public async Task<(IReadOnlyList<DoctorProfile> Doctors, int TotalCount)> GetApprovedPagedAsync(DoctorFilterParams filters, CancellationToken ct)
        {
            var query = context.DoctorProfiles
                .AsNoTracking() // read-only list
                .Include(d => d.Specialty)
                .Include(d => d.User)
                .Where(d => d.IsApprovedByAdmin && d.User.IsActive) // only show approved doctors with active accounts
                .AsQueryable();

            query = ApplyFilters(query, filters);

            var totalCount = await query.CountAsync(ct);

            var doctors = await query
                .Page(filters.PageNumber, filters.PageSize)
                .ToListAsync(ct); // ct is important here to allow cancellation if user navigates away before data loads

            return (doctors, totalCount);
        }
        public async Task<(IReadOnlyList<DoctorProfile> Doctors, int TotalCount)> GetPendingApprovalsAsync(int pageNumber, int? pageSize, CancellationToken ct) // this is for admin dashboard to show doctors waiting for approval
        {
            var query = context.DoctorProfiles
                .AsNoTracking()
                .Include(d => d.Specialty)
                .Include(d => d.User)
                .Where(d => !d.IsApprovedByAdmin && d.User.IsActive)
                .OrderBy(d => d.User.CreatedAt)
                .ThenBy(d => d.Id);

            var totalCount = await query.CountAsync(ct);

            var doctors = pageSize is null
                ? await query.ToListAsync(ct)
                : await query.Page(pageNumber, pageSize.Value).ToListAsync(ct);

            return (doctors, totalCount);
        }

        public async Task<bool> ExistsAsync(int id, CancellationToken ct) // lightweight check for existence
            => await context.DoctorProfiles.AnyAsync(d => d.Id == id, ct);

        public async Task<IReadOnlyList<DoctorSchedule>> GetSchedulesAsync(int doctorProfileId, CancellationToken ct)
            => await context.DoctorSchedules
                .AsNoTracking()
                .Where(s => s.DoctorProfileId == doctorProfileId)
                .OrderBy(s => s.DayOfWeek)
                .ToListAsync(ct);
        public async Task<DoctorSchedule?> GetScheduleByUserIdAndDayAsync(Guid userId, DayOfWeek day, CancellationToken ct)
        {
            var doctor = await context.DoctorProfiles
         .FirstOrDefaultAsync(d => d.UserId == userId, ct);

            if (doctor is null) return null;

            return await context.DoctorSchedules
                .FirstOrDefaultAsync(s => s.DoctorProfileId == doctor.Id && s.DayOfWeek == day, ct);
        }
        public async Task<DoctorSchedule?> GetScheduleByIdAsync(int scheduleId, CancellationToken ct)
            => await context.DoctorSchedules
                  .Include(s => s.DoctorProfile)
                  .FirstOrDefaultAsync(s => s.Id == scheduleId, ct);

        public async Task<IReadOnlyList<DoctorSchedule>> GetActiveSchedulesAsync(CancellationToken ct)
            => await context.DoctorSchedules
                  .AsNoTracking()
                  .Where(s => s.IsActive)
                  .OrderBy(s => s.Id)
                  .ToListAsync(ct);

        public void AddSchedule(DoctorSchedule schedule)
            => context.DoctorSchedules.Add(schedule);

        public void UpdateSchedule(DoctorSchedule schedule)
        {
            context.DoctorSchedules.Update(schedule);
        }

        public void DeleteSchedule(DoctorSchedule schedule)
        {
            context.DoctorSchedules.Remove(schedule);
        }

        public async Task AddAsync(DoctorProfile doctor, CancellationToken ct)
            => await context.DoctorProfiles.AddAsync(doctor, ct);

        public void Update(DoctorProfile doctor)
            => context.DoctorProfiles.Update(doctor);

        // this method is called after a new review is added or an existing review is updated/deleted to recalculate the average rating
        // and total reviews for the doctor. We mark only these two properties as modified to avoid unnecessary updates to other fields.
        public void UpdateRatingCache(DoctorProfile doctor)
        {
            context.Entry(doctor).Property(d => d.AverageRating).IsModified = true;
            context.Entry(doctor).Property(d => d.TotalReviews).IsModified = true;
        }



        #region Helpers 

        private IQueryable<DoctorProfile> ApplyFilters(IQueryable<DoctorProfile> query, DoctorFilterParams filters)
        {
            if (filters.SpecialtyId.HasValue)
                query = query.Where(d => d.SpecialtyId == filters.SpecialtyId.Value);

            if (!string.IsNullOrEmpty(filters.City))
                query = query.Where(d => d.ClinicCity!.Contains(filters.City)); // SQL Server collation is case-insensitive

            if (!string.IsNullOrEmpty(filters.Name))
                query = query.Where(x =>
                        (x.User.FirstName + " " + x.User.LastName)
                        .Contains(filters.Name));

            query = filters.SortBy switch
            {
                DoctorSortBy.Rating => query.OrderByDescending(d => d.AverageRating).ThenBy(d => d.Id),
                DoctorSortBy.Fee => query.OrderBy(d => d.ConsultationFee).ThenBy(d => d.Id),
                DoctorSortBy.Experience => query.OrderByDescending(d => d.YearsOfExperience).ThenBy(d => d.Id),
                _ => query.OrderByDescending(d => d.AverageRating).ThenBy(d => d.Id) // unique tie-breaker keeps paging stable
            };

            return query;
        }

        #endregion
    }
}
