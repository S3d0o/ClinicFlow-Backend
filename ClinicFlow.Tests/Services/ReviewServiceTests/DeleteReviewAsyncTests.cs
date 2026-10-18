using Domain.Entities.AppModule;
using NSubstitute;
using Shared.Errors;

namespace ClinicFlow.Tests.Services.ReviewServiceTests
{
    public class DeleteReviewAsyncTests : ReviewServiceTestsBase
    {
        private const int _reviewId = 1;
        private const int _doctorProfileId = 5;
        private readonly CancellationToken _ct = CancellationToken.None;
        private readonly Guid _authorUserId = Guid.NewGuid();
        private readonly Review _review;

        public DeleteReviewAsyncTests()
        {
            _review = new Review
            {
                Id = _reviewId,
                DoctorProfileId = _doctorProfileId,
                Patient = new PatientProfile { UserId = _authorUserId }
            };
            _uow.Reviews.GetByIdAsync(_reviewId, _ct).Returns(_review);
        }

        [Fact]
        public async Task DeleteReviewAsync_WhenReviewNotFound_ReturnsNotFoundError()
        {
            var result = await _sut.DeleteReviewAsync(999, _authorUserId, "Patient", _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(ReviewErrors.NotFound(999), result.Errors);
        }

        [Fact]
        public async Task DeleteReviewAsync_WhenAuthorPatient_DeletesReview()
        {
            var result = await _sut.DeleteReviewAsync(_reviewId, _authorUserId, "Patient", _ct);

            Assert.True(result.IsSuccess);
            _uow.Reviews.Received(1).Delete(_review);
            // The delete and the rating refresh commit together (REVIEW.md L8)
            Received.InOrder(() =>
            {
                _uow.SaveChangesAsync(_ct);
                _uow.Doctors.RefreshRatingCacheAsync(_doctorProfileId, _ct);
                _transaction.CommitAsync(_ct);
            });
        }

        [Fact]
        public async Task DeleteReviewAsync_WhenAdmin_DeletesReview()
        {
            var result = await _sut.DeleteReviewAsync(_reviewId, Guid.NewGuid(), "Admin", _ct);

            Assert.True(result.IsSuccess);
            _uow.Reviews.Received(1).Delete(_review);
        }

        [Fact]
        public async Task DeleteReviewAsync_WhenOtherPatient_ReturnsUnauthorizedError()
        {
            var result = await _sut.DeleteReviewAsync(_reviewId, Guid.NewGuid(), "Patient", _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(ReviewErrors.Unauthorized, result.Errors);
            _uow.Reviews.DidNotReceive().Delete(Arg.Any<Review>());
        }

        [Fact]
        public async Task DeleteReviewAsync_WhenDoctor_ReturnsUnauthorizedError()
        {
            var result = await _sut.DeleteReviewAsync(_reviewId, Guid.NewGuid(), "Doctor", _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(ReviewErrors.Unauthorized, result.Errors);
            _uow.Reviews.DidNotReceive().Delete(Arg.Any<Review>());
        }
    }
}
