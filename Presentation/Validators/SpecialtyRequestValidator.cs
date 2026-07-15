
using FluentValidation;
using Shared.DTOs.Specialty;

namespace Presentation.Validators
{
    public class SpecialtyRequestValidator : AbstractValidator<SpecialtyRequest>
    {
        public SpecialtyRequestValidator()
        {
            RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Specialty name is required.")
            .MaximumLength(100).WithMessage("Specialty name cannot exceed 100 characters.");

            RuleFor(x => x.Description)
                .MaximumLength(500).WithMessage("Description cannot exceed 500 characters.")
                .When(x => x.Description is not null);

            RuleFor(x => x.IconUrl)
                .Must(uri => Uri.TryCreate(uri, UriKind.Absolute, out _))
                .WithMessage("IconUrl must be a valid URL.")
                .When(x => x.IconUrl is not null);
        }
    }
}
