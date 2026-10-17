using Shared.DTOs.Notification;

namespace Presentation.Validators
{
    public class NotificationFilterRequestValidator : AbstractValidator<NotificationFilterRequest>
    {
        public NotificationFilterRequestValidator()
        {
            RuleFor(x => x.PageNumber)
                .GreaterThan(0).WithMessage("PageNumber must be at least 1.");

            RuleFor(x => x.PageSize)
                .InclusiveBetween(1, 100).WithMessage("PageSize must be between 1 and 100.");
        }
    }
}
