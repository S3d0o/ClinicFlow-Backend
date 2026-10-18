
namespace Persistence.Repositories
{
    public class ReviewRepo(ClinicDbContext context) : IReviewRepo
    {
        public async Task<Review?> GetByIdAsync(int id, CancellationToken ct)
            => await context.Reviews
            .Include(r => r.Patient)
                .ThenInclude(p => p.User)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
        public async Task<Review?> GetByAppointmentIdAsync(int appointmentId, CancellationToken ct)
            => await context.Reviews
                .AsNoTracking()
                .Include(r=>r.Appointment)
                .FirstOrDefaultAsync(r => r.AppointmentId == appointmentId, ct);

        public async Task<(IReadOnlyList<Review> Reviews, int TotalCount)> GetByDoctorIdAsync(int doctorProfileId, ReviewFilterParams filters, CancellationToken ct)
        {
            var query = context.Reviews
                .AsNoTracking()
                .Include(r => r.Patient)
                    .ThenInclude(p => p.User)
                .Where(r => r.DoctorProfileId == doctorProfileId && r.IsVisible)
                .OrderByDescending(r => r.CreatedAt)
                .ThenByDescending(r => r.Id); // unique tie-breaker keeps paging stable

            var totalCount = await query.CountAsync(ct);

            var reviews = await query
                .Page(filters.PageNumber, filters.PageSize)
                .ToListAsync(ct);

            return (reviews, totalCount);
        }

        public async Task AddAsync(Review review, CancellationToken ct)
            => await context.Reviews.AddAsync(review, ct);

        public void Delete(Review review)
            => context.Reviews.Remove(review);
    }
}
