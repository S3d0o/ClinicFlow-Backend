using Shared.DTOs.Entitlements;

namespace Presentation.Validators
{
    public class ChangePlanRequestValidator : AbstractValidator<ChangePlanRequest>
    {
        public ChangePlanRequestValidator()
        {
            RuleFor(x => x.PlanCode)
                .NotEmpty().WithMessage("Plan code is required.")
                .MaximumLength(32).WithMessage("Plan code cannot exceed 32 characters.");
        }
    }
}
