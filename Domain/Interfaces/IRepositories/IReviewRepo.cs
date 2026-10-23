
namespace Persistence.Repositories
{
    public interface IReviewRepo
    {
        // Queries
        Task<Review?> GetByIdAsync(int id, CancellationToken ct);
        Task<Review?> GetByAppointmentIdAsync(int appointmentId, CancellationToken ct);
        Task<(IReadOnlyList<Review> Reviews, int TotalCount)> GetByDoctorIdAsync(int doctorProfileId, ReviewFilterParams filters, CancellationToken ct);

        // Write
        // Adds and saves the review. False when the appointment already has one: a concurrent submission won the
        // unique index on AppointmentId. Any other failure throws
        Task<bool> TryAddAsync(Review review, CancellationToken ct);
        void Delete(Review review);
    }
}
