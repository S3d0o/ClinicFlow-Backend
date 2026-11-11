using Shared.DTOs.Messaging;

namespace Services.Abstraction.Contracts
{
    // The organization's message templates (FR-NTF-02; API_CONTRACT.md §13.28), behind notifications.templates.
    // Templates belong to the organization, so there is no record scope to check
    public interface IMessageTemplateService
    {
        // By key, channel and language
        Task<Result<IReadOnlyList<MessageTemplateResponse>>> ListAsync(MessageTemplateFilterRequest filter, CancellationToken ct);

        // Messages sent from then on use the new text. A WhatsApp template can't be edited
        Task<Result<MessageTemplateResponse>> UpdateAsync(int templateId, MessageTemplateRequest request, CancellationToken ct);

        // The unsaved text with sample values; saves nothing. Fails as UpdateAsync would
        Task<Result<TemplatePreviewResponse>> PreviewAsync(int templateId, MessageTemplateRequest request, CancellationToken ct);

        // Back to the text the system ships
        Task<Result<MessageTemplateResponse>> ResetAsync(int templateId, CancellationToken ct);
    }
}
