using Shared.DTOs.Admin;

namespace Presentation.Validators
{
    public class PendingDoctorsFilterRequestValidator : AbstractValidator<PendingDoctorsFilterRequest>
    {
        public PendingDoctorsFilterRequestValidator()
        {
            RuleFor(x => x.PageNumber)
                .GreaterThan(0).WithMessage("PageNumber must be at least 1.");

            RuleFor(x => x.PageSize)
                .InclusiveBetween(1, 100).WithMessage("PageSize must be between 1 and 100.")
                .When(x => x.PageSize is not null);
        }
    }
}
