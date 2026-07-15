using Shared.DTOs.Review;

namespace Services.Abstraction.Contracts
{
    public interface IReviewService
    {
        Task<Result<ReviewResponse>> SubmitReviewAsync(Guid patientUserId, SubmitReviewRequest request, CancellationToken ct);
        Task<Result<List<ReviewResponse>>> GetDoctorReviewsAsync(int doctorProfileId, ReviewFilterRequest request, CancellationToken ct);
        Task<Result> DeleteReviewAsync(int reviewId, Guid userId, string role, CancellationToken ct);
    }
}

