using Shared.DTOs.Organization;

namespace Presentation.Validators
{
    // API_CONTRACT.md §13.23 BranchInput. The phone is kept as entered until P1 PR 9's normaliser stores E.164
    public class BranchRequestValidator : AbstractValidator<BranchRequest>
    {
        // An Egyptian mobile written locally (01 then 0, 1, 2 or 5, then 8 digits), or any E.164 number (landlines)
        private const string BranchPhonePattern = @"^(01[0125]\d{8}|\+?[1-9]\d{6,14})$";

        public BranchRequestValidator()
        {
            RuleFor(x => x.Code)
                .NotEmpty().WithMessage("Code is required.")
                .Matches("^[A-Z]{2,4}$").WithMessage("Code must be 2 to 4 capital letters.");
            RuleFor(x => x.NameEn).NotEmpty().WithMessage("English name is required.").MaximumLength(80);
            RuleFor(x => x.NameAr).NotEmpty().WithMessage("Arabic name is required.").MaximumLength(80);
            RuleFor(x => x.AddressEn).NotEmpty().WithMessage("English address is required.").MaximumLength(200);
            RuleFor(x => x.AddressAr).NotEmpty().WithMessage("Arabic address is required.").MaximumLength(200);
            RuleFor(x => x.Phone)
                .NotEmpty().WithMessage("Phone is required.")
                .Matches(BranchPhonePattern).WithMessage("Phone must be an Egyptian mobile or an international number.");
            RuleFor(x => x.TimeZone)
                .NotEmpty().WithMessage("Time zone is required.")
                .Must(zone => TimeZoneInfo.TryFindSystemTimeZoneById(zone, out _)).WithMessage("Time zone must be an IANA zone, such as Africa/Cairo.");
        }
    }
}
