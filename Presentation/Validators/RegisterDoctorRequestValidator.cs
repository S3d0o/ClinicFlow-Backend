using Services.Abstraction.Contracts;
using Shared.DTOs.Auth;

namespace Presentation.Validators
{
    public class RegisterDoctorRequestValidator : AbstractValidator<RegisterDoctorRequest>
    {
        public RegisterDoctorRequestValidator(IClock clock)
        {
            RuleFor(x => x.FirstName)
                .NotEmpty().WithMessage("First name is required.")
                .MaximumLength(50).WithMessage("First name cannot exceed 50 characters.");

            RuleFor(x => x.LastName)
                .NotEmpty().WithMessage("Last name is required.")
                .MaximumLength(50).WithMessage("Last name cannot exceed 50 characters.");

            RuleFor(x => x.PhoneNumber)
                .NotEmpty().WithMessage("Phone number is required.")
                .ValidPhoneNumber();

            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Email is required.")
                .EmailAddress().WithMessage("Email must be a valid email address.");

            RuleFor(x => x.Password)
                .StrongPassword();

            RuleFor(x => x.DateOfBirth)
                .LessThan(_ => clock.ClinicToday.AddYears(-18)).WithMessage("You must be at least 18 years old.")
                .When(x => x.DateOfBirth.HasValue);

            // REVIEW.md M13: these used to reach the database and fail as a generic 500
            RuleFor(x => x.SpecialtyId)
                .GreaterThan(0).WithMessage("SpecialtyId must be valid.");

            RuleFor(x => x.YearsOfExperience)
                .InclusiveBetween(0, 70).WithMessage("Years of experience must be between 0 and 70.");

            // Same range as a profile update
            RuleFor(x => x.ConsultationFee)
                .GreaterThan(0).WithMessage("Consultation fee must be greater than 0.")
                .LessThanOrEqualTo(10000).WithMessage("Consultation fee cannot exceed 10,000 EGP.");

            RuleFor(x => x.Bio)
                .MaximumLength(500).WithMessage("Bio cannot exceed 500 characters.")
                .When(x => x.Bio is not null);

            RuleFor(x => x.ClinicAddress)
                .MaximumLength(300).WithMessage("Clinic address cannot exceed 300 characters.")
                .When(x => x.ClinicAddress is not null);

            RuleFor(x => x.ClinicCity)
                .MaximumLength(100).WithMessage("Clinic city cannot exceed 100 characters.")
                .When(x => x.ClinicCity is not null);
        }
    }
}
