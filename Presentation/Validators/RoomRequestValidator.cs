using Shared.DTOs.Organization;

namespace Presentation.Validators
{
    // API_CONTRACT.md §13.23 RoomInput
    public class RoomRequestValidator : AbstractValidator<RoomRequest>
    {
        public RoomRequestValidator()
        {
            RuleFor(x => x.NameEn).NotEmpty().WithMessage("English name is required.").MaximumLength(60);
            RuleFor(x => x.NameAr).NotEmpty().WithMessage("Arabic name is required.").MaximumLength(60);
            RuleFor(x => x.Kind).IsInEnum().WithMessage("Kind must be Consultation, Procedure or Triage.");
        }
    }
}
