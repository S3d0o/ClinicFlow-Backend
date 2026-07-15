
namespace Presentation.Validators
{
    public class UpdatePatientProfileRequestValidator : AbstractValidator<UpdatePatientProfileRequest>
    {
        public UpdatePatientProfileRequestValidator()
        {
            RuleFor(x => x.BloodType)
                .IsInEnum().WithMessage("BloodType must be a valid value.")
                .When(x => x.BloodType is not null);

            RuleFor(x => x.Allergies)
                .MaximumLength(500).WithMessage("Allergies cannot exceed 500 characters.")
                .When(x => x.Allergies is not null);

            RuleFor(x => x.ChronicConditions)
                .MaximumLength(500).WithMessage("Chronic conditions cannot exceed 500 characters.")
                .When(x => x.ChronicConditions is not null);

            RuleFor(x => x.EmergencyContactName)
                .MaximumLength(100).WithMessage("Emergency contact name cannot exceed 100 characters.")
                .When(x => x.EmergencyContactName is not null);

            RuleFor(x => x.EmergencyContactPhone)
                .Matches(@"^\+?[0-9]{7,15}$").WithMessage("Emergency contact phone must be a valid format.")
                .When(x => x.EmergencyContactPhone is not null);
        }
    }
}
