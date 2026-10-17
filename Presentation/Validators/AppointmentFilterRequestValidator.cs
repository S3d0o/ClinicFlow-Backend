using Shared.DTOs.Appointment;

namespace Presentation.Validators
{
    // REVIEW.md H6: PageNumber 0 used to reach Skip(-n) and return 500
    public class AppointmentFilterRequestValidator : AbstractValidator<AppointmentFilterRequest>
    {
        public AppointmentFilterRequestValidator()
        {
            RuleFor(x => x.Status)
                .IsInEnum().WithMessage("Status must be a valid appointment status.")
                .When(x => x.Status is not null);

            RuleFor(x => x.PageNumber)
                .GreaterThan(0).WithMessage("PageNumber must be at least 1.");

            RuleFor(x => x.PageSize)
                .InclusiveBetween(1, 50).WithMessage("PageSize must be between 1 and 50.");
        }
    }
}
