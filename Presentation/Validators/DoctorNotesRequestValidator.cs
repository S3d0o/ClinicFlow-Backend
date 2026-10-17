using Shared.Errors;

namespace Presentation.Validators
{
    public class DoctorNotesRequestValidator : AbstractValidator<DoctorNotesRequest>
    {
        public DoctorNotesRequestValidator()
        {
            RuleFor(x => x.notes)
                .MaximumLength(4000).WithMessage("Notes cannot exceed 4000 characters.")
                .When(x => x.notes is not null);
        }
    }
}
