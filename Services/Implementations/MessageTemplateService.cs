using Domain.Entities.Messaging;
using Domain.Enums;
using Services.Messaging;
using Shared.DTOs.Messaging;
using Shared.Messaging;

namespace Services.Implementations
{
    // The organization's message templates (FR-NTF-02; API_CONTRACT.md §13.28). What a message may contain (its
    // placeholders, the required ones, its default text) comes from MessageTemplateCatalog; the rows hold the text the
    // organization sends. Rows are audited, and messages read them when they are sent, so an edit applies at once
    public class MessageTemplateService(
        IUnitOfWork uow,
        IClock clock,
        ILogger<MessageTemplateService> logger) : IMessageTemplateService
    {
        public async Task<Result<IReadOnlyList<MessageTemplateResponse>>> ListAsync(MessageTemplateFilterRequest filter, CancellationToken ct)
        {
            var templates = await uow.MessageTemplates.ListAsync(filter.Key, filter.Channel, filter.Language, ct);

            // A row whose message the catalog no longer has is never sent, so it isn't listed
            return Result<IReadOnlyList<MessageTemplateResponse>>.Ok(
                [.. from template in templates
                    let definition = MessageTemplateCatalog.Find(template.Key)
                    where definition is not null
                    select ToResponse(template, definition)]);
        }

        public async Task<Result<MessageTemplateResponse>> UpdateAsync(int templateId, MessageTemplateRequest request, CancellationToken ct)
        {
            var draft = await DraftAsync(templateId, request, ct);
            if (draft.IsFailure)
                return draft.Errors[0];
            var (template, definition, subject, body) = draft.Value;

            // The same text again changes nothing: no save, so no audit entry and no new UpdatedAt
            if (template.Subject == subject && template.Body == body)
                return ToResponse(template, definition);

            var isDefault = definition.DefaultFor(template.Channel, template.Language) is { } shipped
                && shipped.Subject == subject && shipped.Body == body;
            template.SetText(subject, body, isDefault, clock.UtcNow);
            await uow.SaveChangesAsync(ct);

            logger.LogInformation("Message template {TemplateId} ({TemplateKey}, {Channel}, {Language}) changed",
                template.Id, template.Key, template.Channel, template.Language.Code());
            return ToResponse(template, definition);
        }

        public async Task<Result<TemplatePreviewResponse>> PreviewAsync(int templateId, MessageTemplateRequest request, CancellationToken ct)
        {
            var draft = await DraftAsync(templateId, request, ct);
            if (draft.IsFailure)
                return draft.Errors[0];
            var (template, _, subject, body) = draft.Value;
            var samples = MessageTemplateCatalog.Samples(template.Language);

            if (template.Channel == TemplateChannel.Email)
            {
                var email = EmailLayout.Render(
                    subject!, body, samples, template.Language, samples[TemplatePlaceholders.ClinicName], clock.UtcNow.Year);
                return new TemplatePreviewResponse(email.Subject, email.TextBody, email.HtmlBody, SmsSegments: null);
            }

            var text = TemplateSyntax.Fill(body, samples);
            return new TemplatePreviewResponse(Subject: null, text, Html: null, SmsSegments.Count(text));
        }

        public async Task<Result<MessageTemplateResponse>> ResetAsync(int templateId, CancellationToken ct)
        {
            var template = await uow.MessageTemplates.GetByIdAsync(templateId, ct);
            if (template is null || MessageTemplateCatalog.Find(template.Key) is not { } definition
                || definition.DefaultFor(template.Channel, template.Language) is not { } shipped)
            {
                logger.LogWarning("Message template {TemplateId} not found for reset", templateId);
                return TemplateErrors.NotFound(templateId);
            }

            if (!template.IsDefault || template.Subject != shipped.Subject || template.Body != shipped.Body)
            {
                template.SetText(shipped.Subject, shipped.Body, isDefault: true, clock.UtcNow);
                await uow.SaveChangesAsync(ct);
                logger.LogInformation("Message template {TemplateId} ({TemplateKey}, {Channel}, {Language}) reset to its default",
                    template.Id, template.Key, template.Channel, template.Language.Code());
            }

            return ToResponse(template, definition);
        }

        // The template with the text the request would give it, checked against what its message allows. Shared by
        // the update and its preview, so a preview fails exactly as the save would
        private async Task<Result<Draft>> DraftAsync(int templateId, MessageTemplateRequest request, CancellationToken ct)
        {
            var template = await uow.MessageTemplates.GetByIdAsync(templateId, ct);
            if (template is null || MessageTemplateCatalog.Find(template.Key) is not { } definition)
            {
                logger.LogWarning("Message template {TemplateId} not found", templateId);
                return TemplateErrors.NotFound(templateId);
            }

            if (template.Channel == TemplateChannel.WhatsApp)
            {
                logger.LogWarning("Message template {TemplateId} is a WhatsApp template and can't be edited", templateId);
                return TemplateErrors.NotEditable;
            }

            // Only an email has a subject; one left out stays as it is. Bodies are stored with \n line ends
            var subject = template.Channel == TemplateChannel.Email ? request.Subject?.Trim() ?? template.Subject : null;
            var body = request.Body.ReplaceLineEndings("\n").Trim();

            if (TemplateSyntax.Validate(definition, subject, body) is { } error)
            {
                logger.LogWarning("Text for message template {TemplateId} rejected: {ErrorCode}", templateId, error.Code);
                return error;
            }

            return new Draft(template, definition, subject, body);
        }

        private sealed record Draft(MessageTemplate Template, TemplateDefinition Definition, string? Subject, string Body);

        private static MessageTemplateResponse ToResponse(MessageTemplate template, TemplateDefinition definition) => new(
            template.Id,
            template.Key,
            template.Channel,
            template.Language,
            template.Subject,
            template.Body,
            definition.Placeholders,
            definition.RequiredPlaceholders,
            Editable: template.Channel != TemplateChannel.WhatsApp,
            template.IsDefault,
            template.UpdatedAt);
    }
}
