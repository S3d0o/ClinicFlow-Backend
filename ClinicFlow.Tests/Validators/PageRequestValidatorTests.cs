using FluentValidation;
using FluentValidation.TestHelper;
using Presentation.Validators;
using Shared.Concurrency;
using Shared.DTOs.Common;

namespace ClinicFlow.Tests.Validators
{
    // The one paging rule of /api/v2 (contract §13.1 "Lists"): defaults 1 and 20, pageSize at most 100
    public class PageRequestValidatorTests
    {
        private readonly PageRequestValidator _validator = new();

        [Fact]
        public void Defaults_AreFirstPageOfTwenty()
        {
            var request = new PageRequest();

            Assert.Equal(1, request.PageNumber);
            Assert.Equal(20, request.PageSize);
            Assert.Equal(0, request.Skip());
        }

        [Fact]
        public void Validate_Defaults_HaveNoErrors()
            => _validator.TestValidate(new PageRequest()).ShouldNotHaveAnyValidationErrors();

        [Theory]
        [InlineData(1, 1)]
        [InlineData(7, 100)]
        public void Validate_InRange_HasNoErrors(int pageNumber, int pageSize)
            => _validator.TestValidate(new PageRequest { PageNumber = pageNumber, PageSize = pageSize }).ShouldNotHaveAnyValidationErrors();

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void Validate_PageNumberBelowOne_FailsOnPageNumber(int pageNumber)
            => _validator.TestValidate(new PageRequest { PageNumber = pageNumber }).ShouldHaveValidationErrorFor(x => x.PageNumber);

        [Theory]
        [InlineData(0)]
        [InlineData(101)]
        public void Validate_PageSizeOutOfRange_FailsOnPageSize(int pageSize)
            => _validator.TestValidate(new PageRequest { PageSize = pageSize }).ShouldHaveValidationErrorFor(x => x.PageSize);

        [Fact]
        public void Skip_ThirdPageOfTwenty_SkipsForty()
            => Assert.Equal(40, new PageRequest { PageNumber = 3, PageSize = 20 }.Skip());

        [Fact]
        public void Validate_DerivedFilter_IsValidatedByTheBaseRule()
        {
            // ValidationFilter hands a derived filter to PageRequestValidator too (ValidationFilterTests covers the filter)
            var result = ((IValidator)_validator).Validate(new ValidationContext<object>(new SampleFilter { PageSize = 500 }));

            Assert.Contains(result.Errors, e => e.PropertyName == nameof(PageRequest.PageSize));
        }

        private sealed record SampleFilter : PageRequest;
    }

    // The rowVersion an update sends back (docs/v2/07_API_DESIGN.md §3)
    public class ValidRowVersionRuleTests
    {
        private sealed record UpdateRequest(string? RowVersion);

        private sealed class UpdateRequestValidator : AbstractValidator<UpdateRequest>
        {
            public UpdateRequestValidator() => RuleFor(x => x.RowVersion).ValidRowVersion();
        }

        private readonly UpdateRequestValidator _validator = new();

        [Fact]
        public void Validate_EncodedRowVersion_HasNoErrors()
            => _validator.TestValidate(new UpdateRequest(RowVersions.Encode([1, 2, 3, 4, 5, 6, 7, 8]))).ShouldNotHaveAnyValidationErrors();

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("abc")]
        [InlineData("AAAAAAAAAA==")]
        public void Validate_MissingOrMalformed_FailsOnRowVersion(string? rowVersion)
            => _validator.TestValidate(new UpdateRequest(rowVersion)).ShouldHaveValidationErrorFor(x => x.RowVersion);
    }
}
