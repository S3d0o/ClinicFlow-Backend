
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
                .ValidPhoneNumber("Emergency contact phone") // same E.164 rule as registration (REVIEW.md M13)
                .When(x => x.EmergencyContactPhone is not null);
        }
    }
}
