using Domain.Entities.AppModule;
using Domain.Entities.IdentityModule;
using Domain.Parameters;
using NSubstitute;
using Shared.DTOs.Review;
using Shared.Errors;

namespace ClinicFlow.Tests.Services.ReviewServiceTests
{
    public class GetDoctorReviewsAsyncTests : ReviewServiceTestsBase
    {
        private const int _doctorId = 5;
        private readonly CancellationToken _ct = CancellationToken.None;

        [Fact]
        public async Task GetDoctorReviewsAsync_WhenDoctorExists_PassesPagingAndMapsPatientNames()
        {
            _uow.Doctors.GetByIdAsync(_doctorId, _ct).Returns(new DoctorProfile { Id = _doctorId });
            IReadOnlyList<Review> reviews =
            [
                new Review
                {
                    Id = 1,
                    Rating = 5,
                    Comment = "Great",
                    IsVisible = true,
                    Patient = new PatientProfile { User = new ApplicationUser { FirstName = "Saad", LastName = "Mohamed" } }
                }
            ];
            _uow.Reviews.GetByDoctorIdAsync(_doctorId, Arg.Any<ReviewFilterParams>(), _ct).Returns((reviews, 1));

            var result = await _sut.GetDoctorReviewsAsync(_doctorId, new ReviewFilterRequest { PageNumber = 3, PageSize = 7 }, _ct);

            Assert.True(result.IsSuccess);
            var review = Assert.Single(result.Value);
            Assert.Equal(1, review.Id);
            Assert.Equal(5, review.Rating);
            Assert.Equal("Great", review.Comment);
            Assert.Equal("Saad Mohamed", review.PatientName);
            await _uow.Reviews.Received(1).GetByDoctorIdAsync(
                _doctorId,
                Arg.Is<ReviewFilterParams>(f => f!.PageNumber == 3 && f.PageSize == 7),
                _ct);
        }

        [Fact]
        public async Task GetDoctorReviewsAsync_WhenDoctorHasNoReviews_ReturnsEmptyList()
        {
            _uow.Doctors.GetByIdAsync(_doctorId, _ct).Returns(new DoctorProfile { Id = _doctorId });
            _uow.Reviews.GetByDoctorIdAsync(_doctorId, Arg.Any<ReviewFilterParams>(), _ct)
                .Returns((Array.Empty<Review>(), 0));

            var result = await _sut.GetDoctorReviewsAsync(_doctorId, new ReviewFilterRequest(), _ct);

            Assert.True(result.IsSuccess);
            Assert.Empty(result.Value);
        }

        [Fact]
        public async Task GetDoctorReviewsAsync_WhenDoctorNotFound_ReturnsDoctorNotFoundError()
        {
            _uow.Doctors.GetByIdAsync(999, _ct).Returns((DoctorProfile?)null);

            var result = await _sut.GetDoctorReviewsAsync(999, new ReviewFilterRequest(), _ct);

            Assert.True(result.IsFailure);
            Assert.Contains(DoctorErrors.NotFound(999), result.Errors);
            await _uow.Reviews.DidNotReceive().GetByDoctorIdAsync(
                Arg.Any<int>(), Arg.Any<ReviewFilterParams>(), Arg.Any<CancellationToken>());
        }
    }
}
