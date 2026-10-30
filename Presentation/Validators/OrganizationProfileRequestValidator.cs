using Shared.DTOs.Organization;

namespace Presentation.Validators
{
    // API_CONTRACT.md §13.24
    public class OrganizationProfileRequestValidator : AbstractValidator<OrganizationProfileRequest>
    {
        public OrganizationProfileRequestValidator()
        {
            RuleFor(x => x.LegalName).NotEmpty().WithMessage("Legal name is required.").MaximumLength(150);
            RuleFor(x => x.BrandNameEn).NotEmpty().WithMessage("English brand name is required.").MaximumLength(80);
            RuleFor(x => x.BrandNameAr).NotEmpty().WithMessage("Arabic brand name is required.").MaximumLength(80);
            RuleFor(x => x.TaxNumber).NotEmpty().WithMessage("Tax number is required.").MaximumLength(30);
            RuleFor(x => x.CommercialRegistration).NotEmpty().WithMessage("Commercial registration is required.").MaximumLength(30);
            RuleFor(x => x.Phone).NotEmpty().WithMessage("Phone is required.").MaximumLength(20);
            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Email is required.")
                .EmailAddress().WithMessage("Email must be a valid email address.")
                .MaximumLength(256);
        }
    }
}
