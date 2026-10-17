using Domain.Enums;
using FluentValidation.TestHelper;
using Presentation.Validators;
using Shared.DTOs.Doctor;
using Shared.DTOs.Review;
using Shared.DTOs.Specialty;

namespace ClinicFlow.Tests.Validators
{
    public class DoctorFilterRequestValidatorTests
    {
        private readonly DoctorFilterRequestValidator _validator = new();

        [Fact]
        public void Validate_Defaults_HaveNoErrors()
            => _validator.TestValidate(new DoctorFilterRequest()).ShouldNotHaveAnyValidationErrors();

        [Theory]
        [InlineData(0, 10)]
        [InlineData(1, 0)]
        [InlineData(1, 51)]
        public void Validate_PagingOutOfRange_Fails(int pageNumber, int pageSize)
            => Assert.False(_validator.TestValidate(new DoctorFilterRequest { PageNumber = pageNumber, PageSize = pageSize }).IsValid);

        [Fact]
        public void Validate_BadSpecialtyCityAndSort_FailOnThoseFields()
        {
            var result = _validator.TestValidate(new DoctorFilterRequest
            {
                SpecialtyId = 0, City = new string('a', 101), SortBy = (DoctorSortBy)9
            });
            result.ShouldHaveValidationErrorFor(x => x.SpecialtyId);
            result.ShouldHaveValidationErrorFor(x => x.City);
            result.ShouldHaveValidationErrorFor(x => x.SortBy);
        }
    }

    public class ReviewFilterRequestValidatorTests
    {
        private readonly ReviewFilterRequestValidator _validator = new();

        [Theory]
        [InlineData(1, 1)]
        [InlineData(3, 20)]
        public void Validate_PagingInRange_HasNoErrors(int pageNumber, int pageSize)
            => _validator.TestValidate(new ReviewFilterRequest { PageNumber = pageNumber, PageSize = pageSize }).ShouldNotHaveAnyValidationErrors();

        [Theory]
        [InlineData(0, 10)]
        [InlineData(1, 0)]
        [InlineData(1, 21)]
        public void Validate_PagingOutOfRange_Fails(int pageNumber, int pageSize)
            => Assert.False(_validator.TestValidate(new ReviewFilterRequest { PageNumber = pageNumber, PageSize = pageSize }).IsValid);
    }

    public class SubmitReviewRequestValidatorTests
    {
        private readonly SubmitReviewRequestValidator _validator = new();

        [Theory]
        [InlineData(1)]
        [InlineData(5)]
        public void Validate_RatingInRange_HasNoErrors(int rating)
            => _validator.TestValidate(new SubmitReviewRequest(10, rating, "Great")).ShouldNotHaveAnyValidationErrors();

        [Theory]
        [InlineData(0)]
        [InlineData(6)]
        public void Validate_RatingOutside1To5_FailsOnRating(int rating)
            => _validator.TestValidate(new SubmitReviewRequest(10, rating, null)).ShouldHaveValidationErrorFor(x => x.Rating);

        [Fact]
        public void Validate_CommentOver300Characters_FailsOnComment()
            => _validator.TestValidate(new SubmitReviewRequest(10, 4, new string('a', 301))).ShouldHaveValidationErrorFor(x => x.Comment);

        [Fact]
        public void Validate_NonPositiveAppointmentId_FailsOnAppointmentId()
            => _validator.TestValidate(new SubmitReviewRequest(0, 4, null)).ShouldHaveValidationErrorFor(x => x.AppointmentId);
    }

    public class SpecialtyRequestValidatorTests
    {
        private readonly SpecialtyRequestValidator _validator = new();

        [Fact]
        public void Validate_NameOnly_HasNoErrors()
            => _validator.TestValidate(new SpecialtyRequest("Neurology", null, null)).ShouldNotHaveAnyValidationErrors();

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void Validate_MissingName_FailsOnName(string? name)
            => _validator.TestValidate(new SpecialtyRequest(name!, null, null)).ShouldHaveValidationErrorFor(x => x.Name);

        [Fact]
        public void Validate_RelativeIconUrlAndLongFields_Fail()
        {
            var result = _validator.TestValidate(new SpecialtyRequest(new string('a', 101), new string('a', 501), "icons/heart.svg"));
            result.ShouldHaveValidationErrorFor(x => x.Name);
            result.ShouldHaveValidationErrorFor(x => x.Description);
            result.ShouldHaveValidationErrorFor(x => x.IconUrl);
        }
    }
}
