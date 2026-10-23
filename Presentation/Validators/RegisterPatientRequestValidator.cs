
using Services.Abstraction.Contracts;
using Shared.DTOs.Auth;

namespace Presentation.Validators
{
    public class RegisterPatientRequestValidator : AbstractValidator<RegisterPatientRequest>
    {
        public RegisterPatientRequestValidator(IClock clock)
        {
            RuleFor(x => x.FirstName)
                .NotEmpty().WithMessage("First name is required.")
                .MaximumLength(50).WithMessage("First name cannot exceed 50 characters.");

            RuleFor(x=>x.LastName)
                .NotEmpty().WithMessage("Last name is required.")
                .MaximumLength(50).WithMessage("Last name cannot exceed 50 characters.");

            RuleFor(x=>x.PhoneNumber)
                .NotEmpty().WithMessage("Phone number is required.")
                .ValidPhoneNumber();

            RuleFor(x=>x.Email)
                .NotEmpty().WithMessage("Email is required.")
                .EmailAddress().WithMessage("Email must be a valid email address.");

            RuleFor(x=>x.Password)
                .StrongPassword();

            RuleFor(x => x.DateOfBirth)
                .LessThan(_ => clock.ClinicToday.AddYears(-18)).WithMessage("You must be at least 18 years old.")
                .When(x => x.DateOfBirth.HasValue);

            RuleFor(x=>x.EmergencyContactPhone)
                .ValidPhoneNumber("Emergency contact phone number")
                .When(x=>x.EmergencyContactPhone is not null);
        }
    }
}
