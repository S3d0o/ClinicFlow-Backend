
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

        public async Task<bool> TryAddAsync(Review review, CancellationToken ct)
        {
            context.Reviews.Add(review);
            try
            {
                await context.SaveChangesAsync(ct);
                return true;
            }
            catch (DbUpdateException)
            {
                // The unique index on AppointmentId; anything else is a real failure
                if (!await context.Reviews.AnyAsync(r => r.AppointmentId == review.AppointmentId, ct))
                    throw;

                context.Entry(review).State = EntityState.Detached;
                return false;
            }
        }

        public void Delete(Review review)
            => context.Reviews.Remove(review);
    }
}
