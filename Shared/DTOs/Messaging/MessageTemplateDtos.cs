using Domain.Enums;
using System.Text.Json.Serialization;

namespace Shared.DTOs.Messaging
{
    // API_CONTRACT.md §13.23 MessageTemplate (FR-NTF-02). Placeholders are the names the key offers, written {{name}};
    // a body must keep the required ones. Editable is false for WhatsApp, whose texts Meta approves. IsDefault: the
    // text is the one the system ships (never edited, or reset)
    public sealed record MessageTemplateResponse(
        int Id,
        string Key,
        TemplateChannel Channel,
        Language Language,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Subject,
        string Body,
        IReadOnlyList<string> Placeholders,
        IReadOnlyList<string> RequiredPlaceholders,
        bool Editable,
        bool IsDefault,
        DateTime UpdatedAt);

    // GET /api/v2/message-templates: each filter is optional
    public sealed record MessageTemplateFilterRequest(string? Key = null, TemplateChannel? Channel = null, Language? Language = null);

    // PUT /api/v2/message-templates/{templateId} and its preview. Subject is the email's; left out, it stays as it is
    public sealed record MessageTemplateRequest(string? Subject, string Body);

    // The text with sample values. Html is the whole email as it would be sent (Email only); SmsSegments how many SMS
    // parts the text takes (Sms only)
    public sealed record TemplatePreviewResponse(
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Subject,
        string Body,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Html,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? SmsSegments);
}
