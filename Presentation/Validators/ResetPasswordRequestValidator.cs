using Shared.DTOs.Auth;

namespace Presentation.Validators
{
    // REVIEW.md M13: the new password follows the registration rules, not only Identity's weaker policy
    public class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
    {
        public ResetPasswordRequestValidator()
        {
            RuleFor(x => x.UserId)
                .NotEmpty().WithMessage("UserId is required.");

            RuleFor(x => x.Token)
                .NotEmpty().WithMessage("Token is required.");

            RuleFor(x => x.NewPassword)
                .StrongPassword();
        }
    }
}
