using Domain.Entities.IdentityModule;
using Domain.Enums;
using Services.Messaging;
using Shared.Messaging;

namespace Services.Implementations
{
    // Writes a message from the organization's template in the recipient's language and hands it to the channel's
    // adapter (FR-NTF-02, R10). The text is the organization's row; without one, or with one that no longer fits the
    // message (a placeholder the catalog dropped), the catalog's default is sent, so a message is never lost to a
    // template
    public class TemplatedMessenger(
        IUnitOfWork uow,
        IEmailService emailService,
        IClock clock,
        ILogger<TemplatedMessenger> logger) : ITemplatedMessenger
    {
        private const string FallbackClinicName = "ClinicFlow";

        public async Task SendEmailAsync(
            ApplicationUser recipient, string templateKey, IReadOnlyDictionary<string, string> values, CancellationToken ct)
        {
            var language = recipient.PreferredLanguage;
            var definition = MessageTemplateCatalog.Find(templateKey)
                ?? throw new InvalidOperationException($"'{templateKey}' is not a message template.");
            var fallback = definition.DefaultFor(TemplateChannel.Email, language)
                ?? throw new InvalidOperationException($"'{templateKey}' is not sent by email.");

            var (subject, body) = (fallback.Subject!, fallback.Body);
            var stored = await uow.MessageTemplates.FindAsync(templateKey, TemplateChannel.Email, language, ct);
            if (stored is null)
                logger.LogWarning("No {TemplateKey} email template in {Language}: the default text is sent", templateKey, language.Code());
            else if (TemplateSyntax.Validate(definition, stored.Subject, stored.Body) is { } error)
                logger.LogError("The {TemplateKey} email template {TemplateId} is invalid ({ErrorCode}): the default text is sent",
                    templateKey, stored.Id, error.Code);
            else
                (subject, body) = (stored.Subject ?? fallback.Subject!, stored.Body);

            var clinicName = await ClinicNameAsync(language, ct);
            var allValues = new Dictionary<string, string>(values) { [TemplatePlaceholders.ClinicName] = clinicName };
            var email = EmailLayout.Render(subject, body, allValues, language, clinicName, clock.UtcNow.Year);

            await emailService.SendAsync(
                new EmailMessage(recipient.Email!, MessageValues.FullName(recipient), email.Subject, email.TextBody, email.HtmlBody, templateKey), ct);
        }

        // The brand name in the message's language, then in the other one, then the legal name
        private async Task<string> ClinicNameAsync(Language language, CancellationToken ct)
        {
            var organization = await uow.Organizations.GetCurrentAsync(ct);
            string?[] names = language == Language.En
                ? [organization.BrandNameEn, organization.BrandNameAr, organization.LegalName]
                : [organization.BrandNameAr, organization.BrandNameEn, organization.LegalName];
            return names.FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? FallbackClinicName;
        }
    }
}
