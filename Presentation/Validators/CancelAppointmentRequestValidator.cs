using Shared.DTOs.Appointment;

namespace Presentation.Validators
{
    public class CancelAppointmentRequestValidator : AbstractValidator<CancelAppointmentRequest>
    {
        public CancelAppointmentRequestValidator()
        {
            RuleFor(x => x.CancellationReason)
                .MaximumLength(500).WithMessage("Cancellation reason cannot exceed 500 characters.")
                .When(x => x.CancellationReason is not null);
        }
    }
}
