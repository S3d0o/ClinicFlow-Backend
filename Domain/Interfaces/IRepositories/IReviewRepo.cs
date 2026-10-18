
namespace Persistence.Repositories
{
    public interface IReviewRepo
    {
        // Queries
        Task<Review?> GetByIdAsync(int id, CancellationToken ct);
        Task<Review?> GetByAppointmentIdAsync(int appointmentId, CancellationToken ct);
        Task<(IReadOnlyList<Review> Reviews, int TotalCount)> GetByDoctorIdAsync(int doctorProfileId, ReviewFilterParams filters, CancellationToken ct);

        // Write
        Task AddAsync(Review review, CancellationToken ct);
        void Delete(Review review);
    }
}
