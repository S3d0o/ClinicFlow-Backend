// Presentation/Controllers/SubmitReviewRequestValidator.cs
using Shared.DTOs.Review;

namespace Presentation.Validators
{
    public class SubmitReviewRequestValidator : AbstractValidator<SubmitReviewRequest>
    {
        public SubmitReviewRequestValidator()
        {
            RuleFor(x => x.AppointmentId)
                .GreaterThan(0).WithMessage("AppointmentId must be a positive integer.");

            RuleFor(x => x.Rating)
                .InclusiveBetween(1, 5).WithMessage("Rating must be an integer between 1 and 5.");

            RuleFor(x => x.Comment)
                .MaximumLength(300).WithMessage("Comment cannot exceed 300 characters.")
                .When(x => !string.IsNullOrEmpty(x.Comment));
        }
    }
}
