using Domain.Enums;
using Domain.Parameters;
using Shared.DTOs.Review;

namespace Services.Implementations
{
    public class ReviewService(
        IUnitOfWork uow,
        IMapper mapper,
        ILogger<ReviewService> logger,
        IClock clock) : IReviewService
    {
        public async Task<Result<ReviewResponse>> SubmitReviewAsync(Guid patientUserId, SubmitReviewRequest request, CancellationToken ct)
        {
            var patient = await uow.Patients.GetPatientByUserIdAsync(patientUserId, ct);
            if (patient == null)
            {
                logger.LogWarning("Patient not found for user ID: {UserId}", patientUserId);
                return PatientErrors.ProfileNotFound(patientUserId);
            }
            // 1. Validate ownership and status first, so a patient can't learn whether
            //    another patient's appointment has been reviewed (REVIEW.md NL22)
            var appointment = await uow.Appointments.GetByIdAsync(request.AppointmentId, ct);
            if (appointment == null)
                return AppointmentErrors.NotFound(request.AppointmentId);

            if (appointment.PatientProfileId != patient.Id)
                return AppointmentErrors.Forbidden;

            if (appointment.Status != AppointmentStatus.Completed)
                return AppointmentErrors.ReviewNotAllowed;

            // 2. Check no review exists yet
            var existingReview = await uow.Reviews.GetByAppointmentIdAsync(request.AppointmentId, ct);
            if (existingReview != null)
                return AppointmentErrors.ReviewAlreadyExists;

            // 3. Create the review
            var review = new Review
            {
                AppointmentId = request.AppointmentId,
                PatientProfileId = patient.Id,
                DoctorProfileId = appointment.DoctorProfileId, // ← from appointment, not existingReview
                Rating = request.Rating,
                Comment = request.Comment,
                IsVisible = true,
                CreatedAt = clock.UtcNow
            };

            // The review and the doctor's rating cache are saved together (REVIEW.md L8)
            return await uow.ExecuteInTransactionAsync<Result<ReviewResponse>>(async transaction =>
            {
                // A concurrent request may have created the review between the check above and this insert
                if (!await uow.Reviews.TryAddAsync(review, ct))
                {
                    logger.LogWarning("Concurrent review submission for appointment {AppointmentId}", request.AppointmentId);
                    return AppointmentErrors.ReviewAlreadyExists;
                }
                await uow.Doctors.RefreshRatingCacheAsync(appointment.DoctorProfileId, ct);
                await transaction.CommitAsync(ct);

                logger.LogInformation("Review submitted for appointment {AppointmentId} by user {UserId}", request.AppointmentId, patientUserId);
                return mapper.Map<ReviewResponse>(review);
            }, ct);
        }

        public async Task<Result> DeleteReviewAsync(int reviewId, Guid userId, string role, CancellationToken ct)
        {
            var review = await uow.Reviews.GetByIdAsync(reviewId, ct);
            if (review == null)
            {
                logger.LogWarning("Review not found for ID: {ReviewId}", reviewId);
                return ReviewErrors.NotFound(reviewId);
            }
            // Only the patient who wrote the review, or an admin, may delete it (doctors must not remove their own reviews)
            var canDelete = role == Roles.Admin || (role == Roles.Patient && review.Patient.UserId == userId);
            if (!canDelete)
            {
                logger.LogWarning("User ID: {UserId} is not authorized to delete review ID: {ReviewId}", userId, reviewId);
                return ReviewErrors.Forbidden;
            }

            return await uow.ExecuteInTransactionAsync(async transaction =>
            {
                uow.Reviews.Delete(review);
                await uow.SaveChangesAsync(ct);
                await uow.Doctors.RefreshRatingCacheAsync(review.DoctorProfileId, ct);
                await transaction.CommitAsync(ct);
                return Result.Ok();
            }, ct);
        }

        public async Task<Result<List<ReviewResponse>>> GetDoctorReviewsAsync(int doctorProfileId, ReviewFilterRequest request, CancellationToken ct)
        {
            if (!await uow.Doctors.ExistsAsync(doctorProfileId, ct))
            {
                logger.LogWarning("Doctor not found for ID: {DoctorProfileId}", doctorProfileId);
                return DoctorErrors.NotFound(doctorProfileId);
            }
            var filters = new ReviewFilterParams
            {
                PageNumber = request.PageNumber,
                PageSize = request.PageSize
            };
            var (reviews, total) = await uow.Reviews.GetByDoctorIdAsync(doctorProfileId, filters, ct);
            return mapper.Map<List<ReviewResponse>>(reviews);
        }
    }
}
