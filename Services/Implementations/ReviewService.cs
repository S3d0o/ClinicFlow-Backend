using Domain.Enums;
using Domain.Parameters;
using Shared.DTOs.Review;

namespace Services.Implementations
{
    public class ReviewService(
        IUnitOfWork uow,
        IMapper mapper,
        ILogger<ReviewService> logger) : IReviewService
    {
        public async Task<Result<ReviewResponse>> SubmitReviewAsync(Guid patientUserId, SubmitReviewRequest request, CancellationToken ct)
        {
            var patient = await uow.Patients.GetPatientByUserIdAsync(patientUserId, ct);
            if (patient == null)
            {
                logger.LogWarning("Patient not found for user ID: {UserId}", patientUserId);
                return PatientErrors.ProfileNotFound(patientUserId);
            }
            // 1. Check no review exists yet
            var existingReview = await uow.Reviews.GetByAppointmentIdAsync(request.AppointmentId, ct);
            if (existingReview != null)
                return AppointmentErrors.ReviewAlreadyExists;

            // 2. Get the actual appointment to validate ownership and status
            var appointment = await uow.Appointments.GetByIdAsync(request.AppointmentId, ct);
            if (appointment == null)
                return AppointmentErrors.NotFound(request.AppointmentId);

            if (appointment.PatientProfileId != patient.Id)
                return AppointmentErrors.Unauthorized;

            if (appointment.Status != AppointmentStatus.Completed)
                return AppointmentErrors.ReviewNotAllowed;

            // 3. Create the review
            var review = new Review
            {
                AppointmentId = request.AppointmentId,
                PatientProfileId = patient.Id,
                DoctorProfileId = appointment.DoctorProfileId, // ← from appointment, not existingReview
                Rating = request.Rating,
                Comment = request.Comment,
                IsVisible = true,
                CreatedAt = DateTime.UtcNow
            };

            var doctor = await uow.Doctors.GetByIdAsync(appointment.DoctorProfileId, ct);
            if (doctor == null)
                return DoctorErrors.NotFound(appointment.DoctorProfileId);

            await uow.Reviews.AddAsync(review, ct);
            await uow.SaveChangesAsync(ct);
            await RecalculateAvgRatingAndTotalReviewsForDoctor(doctor, ct);

            logger.LogInformation("Review submitted for appointment {AppointmentId} by user {UserId}", request.AppointmentId, patientUserId);
            return mapper.Map<ReviewResponse>(review);

        }

        public async Task<Result> DeleteReviewAsync(int reviewId, Guid userId, string role, CancellationToken ct)
        {
            var review = await uow.Reviews.GetByIdAsync(reviewId, ct);
            if (review == null)
            {
                logger.LogWarning("Review not found for ID: {ReviewId}", reviewId);
                return ReviewErrors.NotFound(reviewId);
            }
            if(role == "Patient" && review.Patient.UserId != userId)
            {
                logger.LogWarning("User ID: {UserId} is not authorized to delete review ID: {ReviewId}", userId, reviewId);
                return ReviewErrors.Unauthorized;
            }

            var doctor = await uow.Doctors.GetByIdAsync(review.DoctorProfileId, ct);
            if (doctor == null)
            {
                logger.LogWarning("Doctor not found for ID: {DoctorProfileId}", review.DoctorProfileId);
                return DoctorErrors.NotFound(review.DoctorProfileId);
            }

             uow.Reviews.Delete(review);
            await uow.SaveChangesAsync(ct);

            await RecalculateAvgRatingAndTotalReviewsForDoctor(doctor, ct);
            return Result.Ok();
        }

        public async Task<Result<List<ReviewResponse>>> GetDoctorReviewsAsync(int doctorProfileId, ReviewFilterRequest request, CancellationToken ct)
        {
            var doctor = await uow.Doctors.GetByIdAsync(doctorProfileId, ct);
            if (doctor == null)
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

        #region Helpers

        private async Task RecalculateAvgRatingAndTotalReviewsForDoctor(Domain.Entities.AppModule.DoctorProfile doctor, CancellationToken ct)
        {
            var average = await uow.Reviews.GetAverageRatingAsync(doctor.Id, ct);
            var total = await uow.Reviews.GetTotalReviewsAsync(doctor.Id, ct);

            doctor.AverageRating = average;
            doctor.TotalReviews = total;
            uow.Doctors.UpdateRatingCache(doctor); // marks only those 2 props as modified
            await uow.SaveChangesAsync(ct);
        }

        #endregion
    }
}
