
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
                .OrderByDescending(r => r.CreatedAt);

            var totalCount = await query.CountAsync(ct);

            var reviews = await query
                .Skip((filters.PageNumber - 1) * filters.PageSize)
                .Take(filters.PageSize)
                .ToListAsync(ct);

            return (reviews, totalCount);
        }

        public async Task AddAsync(Review review, CancellationToken ct)
            => await context.Reviews.AddAsync(review, ct);

        public void Delete(Review review)
            => context.Reviews.Remove(review);

        public async Task<double> GetAverageRatingAsync(int doctorProfileId, CancellationToken ct)
        {
            var averageRating = await context.Reviews
                 .AsNoTracking()
                 .Where(r => r.DoctorProfileId == doctorProfileId && r.IsVisible)
                 .AverageAsync(r => (double?)r.Rating, ct) ?? 0.0;
            return averageRating;
        }

        public async Task<int> GetTotalReviewsAsync(int doctorProfileId, CancellationToken ct)
        {
            var totalReviews = await context.Reviews
                .AsNoTracking()
                .Where(r => r.DoctorProfileId == doctorProfileId && r.IsVisible)
                .CountAsync(ct);

            return totalReviews;
        }
    }
}
