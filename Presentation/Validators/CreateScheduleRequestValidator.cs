// Presentation/Controllers/CreateScheduleRequestValidator.cs
using Shared.DTOs.Doctor;

namespace Presentation.Validators
{
    public class CreateScheduleRequestValidator : AbstractValidator<CreateScheduleRequest>
    {
        public CreateScheduleRequestValidator()
        {
            RuleFor(x => x.DayOfWeek)
                .IsInEnum().WithMessage("DayOfWeek must be a valid day.");

            RuleFor(x => x.StartTime)
                .NotEqual(default(TimeOnly)).WithMessage("StartTime is required.");

            RuleFor(x => x.EndTime)
                .NotEqual(default(TimeOnly)).WithMessage("EndTime is required.")
                .GreaterThan(x => x.StartTime).WithMessage("EndTime must be after StartTime.");

            RuleFor(x => x.SlotDurationMinutes)
                .GreaterThanOrEqualTo(10).WithMessage("Slot duration must be at least 10 minutes.")
                .LessThanOrEqualTo(120).WithMessage("Slot duration cannot exceed 120 minutes.");

            // Ensure at least one full slot fits in the working window
            RuleFor(x => x)
                .Must(x => (x.EndTime.ToTimeSpan() - x.StartTime.ToTimeSpan()).TotalMinutes >= x.SlotDurationMinutes)
                .WithMessage("The time window must be large enough to fit at least one slot.")
                .OverridePropertyName("SlotDurationMinutes");
        }
    }
}
