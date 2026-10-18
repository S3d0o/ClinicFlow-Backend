using Shared.DTOs.Appointment;

namespace Presentation.Validators
{
    public class DoctorNotesRequestValidator : AbstractValidator<DoctorNotesRequest>
    {
        public DoctorNotesRequestValidator()
        {
            RuleFor(x => x.Notes)
                .MaximumLength(4000).WithMessage("Notes cannot exceed 4000 characters.")
                .When(x => x.Notes is not null);
        }
    }
}
