// Presentation/Controllers/DoctorFilterRequestValidator.cs
namespace Presentation.Validators
{
    public class DoctorFilterRequestValidator : AbstractValidator<DoctorFilterRequest>
    {
        public DoctorFilterRequestValidator()
        {
            RuleFor(x => x.SpecialtyId)
                .GreaterThan(0).WithMessage("SpecialtyId must be valid.")
                .When(x => x.SpecialtyId is not null);

            RuleFor(x => x.City)
                .MaximumLength(100).WithMessage("City cannot exceed 100 characters.")
                .When(x => x.City is not null);

            RuleFor(x => x.PageNumber)
                .GreaterThan(0).WithMessage("PageNumber must be at least 1.");

            RuleFor(x => x.PageSize)
                .InclusiveBetween(1, 50).WithMessage("PageSize must be between 1 and 50.");

            RuleFor(x => x.SortBy)
                .IsInEnum().WithMessage("SortBy must be a valid sort option.");
        }

    }
}
