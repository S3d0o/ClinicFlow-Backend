using Shared.DTOs.Review;

namespace Presentation.Validators
{
    public class ReviewFilterRequestValidator : AbstractValidator<ReviewFilterRequest>
    {
        public ReviewFilterRequestValidator()
        {
            RuleFor(x => x.PageNumber)
                .GreaterThan(0).WithMessage("PageNumber must be a positive integer.");

            RuleFor(x => x.PageSize)
                .GreaterThan(0).WithMessage("PageSize must be a positive integer.")
                .LessThanOrEqualTo(20).WithMessage("PageSize cannot exceed 20.");

        }
    }
}
