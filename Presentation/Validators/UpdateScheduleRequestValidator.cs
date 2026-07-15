
namespace Presentation.Validators
{
    public class UpdateScheduleRequestValidator : AbstractValidator<UpdateScheduleRequest>
    {
        public UpdateScheduleRequestValidator()
        {
            RuleFor(x => x.StartTime)
                .NotEqual(default(TimeOnly)).WithMessage("StartTime cannot be empty when provided.")
                .When(x => x.StartTime is not null);

            RuleFor(x => x.EndTime)
                .NotEqual(default(TimeOnly)).WithMessage("EndTime cannot be empty when provided.")
                .When(x => x.EndTime is not null);

            // Only compare if both are provided
            RuleFor(x => x)
                .Must(x => x.EndTime! > x.StartTime!)
                .WithMessage("EndTime must be after StartTime.")
                .When(x => x.StartTime is not null && x.EndTime is not null)
                .OverridePropertyName("EndTime");

            RuleFor(x => x.SlotDurationMinutes)
                .GreaterThanOrEqualTo(10).WithMessage("Slot duration must be at least 10 minutes.")
                .LessThanOrEqualTo(120).WithMessage("Slot duration cannot exceed 120 minutes.")
                .When(x => x.SlotDurationMinutes is not null);
        }
    }
}
