using FluentValidation;
using Shared.DTOs.Profile;

namespace Presentation.Validators;

public class UpdateUserProfileRequestValidator : AbstractValidator<UpdateUserProfileRequest>
{
    public UpdateUserProfileRequestValidator()
    {
        RuleFor(x => x.FirstName)
            .MaximumLength(50).WithMessage("First name cannot exceed 50 characters.")
            .When(x => x.FirstName is not null);

        RuleFor(x => x.LastName)
            .MaximumLength(50).WithMessage("Last name cannot exceed 50 characters.")
            .When(x => x.LastName is not null);

        RuleFor(x => x.PhoneNumber)
            .ValidPhoneNumber() // same E.164 rule as registration (REVIEW.md M13)
            .When(x => x.PhoneNumber is not null);

        RuleFor(x => x.ProfilePictureUrl)
            .AbsoluteHttpUrl("Profile picture URL")
            .When(x => x.ProfilePictureUrl is not null);

        RuleFor(x => x.Gender)
            .IsInEnum().WithMessage("Gender must be a valid value.")
            .When(x => x.Gender is not null);

        RuleFor(x => x.DateOfBirth)
            .Must(dob => dob < DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("Date of birth must be in the past.")
            .When(x => x.DateOfBirth is not null);
    }
}