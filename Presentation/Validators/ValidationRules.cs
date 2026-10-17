namespace Presentation.Validators
{
    // Rules shared by several requests, so registration, profile updates and password reset agree (REVIEW.md M13)
    public static class ValidationRules
    {
        // E.164: optional "+", then 7 to 15 digits with no leading zero
        public const string PhonePattern = @"^\+?[1-9]\d{6,14}$";

        public static IRuleBuilderOptions<T, string?> ValidPhoneNumber<T>(this IRuleBuilder<T, string?> rule, string label = "Phone number")
            => rule.Matches(PhonePattern).WithMessage($"{label} must be in E.164 format.");

        public static IRuleBuilderOptions<T, string> StrongPassword<T>(this IRuleBuilder<T, string> rule)
            => rule
                .NotEmpty().WithMessage("Password is required.")
                .MinimumLength(8).WithMessage("Password must be at least 8 characters long.")
                .Matches(@"[A-Z]").WithMessage("Password must contain at least one uppercase letter.")
                .Matches(@"[a-z]").WithMessage("Password must contain at least one lowercase letter.")
                .Matches(@"\d").WithMessage("Password must contain at least one digit.")
                .Matches(@"[\W_]").WithMessage("Password must contain at least one special character.");
    }
}
