using Microsoft.EntityFrameworkCore;
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
            // 1. Validate ownership and status first, so a patient can't learn whether
            //    another patient's appointment has been reviewed (REVIEW.md NL22)
            var appointment = await uow.Appointments.GetByIdAsync(request.AppointmentId, ct);
            if (appointment == null)
                return AppointmentErrors.NotFound(request.AppointmentId);

            if (appointment.PatientProfileId != patient.Id)
                return AppointmentErrors.Unauthorized;

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
                CreatedAt = DateTime.UtcNow
            };

            var doctor = await uow.Doctors.GetByIdAsync(appointment.DoctorProfileId, ct);
            if (doctor == null)
                return DoctorErrors.NotFound(appointment.DoctorProfileId);

            await uow.Reviews.AddAsync(review, ct);
            try
            {
                await uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                // A concurrent request may have created the review between the check above and this insert
                // (unique index on AppointmentId); anything else is a real failure
                if (await uow.Reviews.GetByAppointmentIdAsync(request.AppointmentId, ct) is null)
                    throw;

                logger.LogWarning(ex, "Concurrent review submission for appointment {AppointmentId}", request.AppointmentId);
                return AppointmentErrors.ReviewAlreadyExists;
            }
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
            // Only the patient who wrote the review, or an admin, may delete it (doctors must not remove their own reviews)
            var canDelete = role == "Admin" || (role == "Patient" && review.Patient.UserId == userId);
            if (!canDelete)
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
