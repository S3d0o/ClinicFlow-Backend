using Shared.DTOs.Messaging;

namespace Presentation.Validators
{
    // API_CONTRACT.md §13.28: the text of a template. Its placeholders are checked by the service, which knows the
    // template's message
    public class MessageTemplateRequestValidator : AbstractValidator<MessageTemplateRequest>
    {
        public MessageTemplateRequestValidator()
        {
            // Left out, an email's subject stays as it is; sent, it can't be blank
            RuleFor(x => x.Subject).NotEmpty().WithMessage("The subject can't be empty.").MaximumLength(200)
                .When(x => x.Subject is not null);
            RuleFor(x => x.Body).NotEmpty().WithMessage("The body is required.").MaximumLength(4000);
        }
    }
}
