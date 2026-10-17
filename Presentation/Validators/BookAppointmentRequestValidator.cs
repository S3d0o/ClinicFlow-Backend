using Shared.DTOs.Appointment;

namespace Presentation.Validators
{
    public class BookAppointmentRequestValidator : AbstractValidator<BookAppointmentRequest>
    {
        public BookAppointmentRequestValidator()
        {
            RuleFor(x => x.SlotId)
                .GreaterThan(0).WithMessage("SlotId must be valid.");

            RuleFor(x => x.ReasonForVisit)
                .MaximumLength(500).WithMessage("Reason for visit cannot exceed 500 characters.")
                .When(x => x.ReasonForVisit is not null);
        }
    }
}
