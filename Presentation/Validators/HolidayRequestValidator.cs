using Shared.DTOs.Organization;

namespace Presentation.Validators
{
    // API_CONTRACT.md §13.23 HolidayInput
    public class HolidayRequestValidator : AbstractValidator<HolidayRequest>
    {
        public HolidayRequestValidator()
        {
            RuleFor(x => x.NameEn).NotEmpty().WithMessage("English name is required.").MaximumLength(80);
            RuleFor(x => x.NameAr).NotEmpty().WithMessage("Arabic name is required.").MaximumLength(80);
            RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).WithMessage("The last day can't be before the first.");
            // A yearly holiday is matched by month and day, so it must be shorter than a year
            RuleFor(x => x.To).LessThan(x => x.From.AddYears(1)).When(x => x.RecursYearly)
                .WithMessage("A yearly holiday must be shorter than a year.");
        }
    }
}
