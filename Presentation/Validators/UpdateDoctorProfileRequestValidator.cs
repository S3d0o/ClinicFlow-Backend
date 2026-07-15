namespace Presentation.Validators
{
    public class UpdateDoctorProfileRequestValidator : AbstractValidator<UpdateDoctorProfileRequest>
    {
        public UpdateDoctorProfileRequestValidator()
        {
            RuleFor(x => x.Bio)
                .MaximumLength(500).WithMessage("Bio cannot exceed 500 characters.")
                .When(x => x.Bio is not null);

            RuleFor(x => x.ConsultationFee)
                .GreaterThan(0).WithMessage("Consultation fee must be greater than 0.")
                .LessThanOrEqualTo(10000).WithMessage("Consultation fee cannot exceed 10,000 EGP.");

            RuleFor(x => x.ClinicAddress)
                .MaximumLength(300).WithMessage("Clinic address cannot exceed 300 characters.")
                .When(x => x.ClinicAddress is not null);

            RuleFor(x => x.ClinicCity)
                .MaximumLength(100).WithMessage("Clinic city cannot exceed 100 characters.")
                .When(x => x.ClinicCity is not null);
        }
    }
}
