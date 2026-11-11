using Presentation.Authorization;
using Services.Abstraction.Contracts;
using Shared.DTOs.Messaging;

namespace Presentation.Controllers.V2
{
    // The texts of the clinic's emails and phone messages (FR-NTF-02; API_CONTRACT.md §13.28)
    [Route(RoutePrefix + "/message-templates")]
    public class MessageTemplatesController(IMessageTemplateService service) : V2ApiController
    {
        [HttpGet]
        [HasPermission(Permissions.Platform.NotificationTemplates)]
        [EndpointSummary("List the message templates")]
        [EndpointDescription(
            "Every message the clinic sends, once per channel and language, by key, channel and language. Each one lists " +
            "the placeholders its text may use, written {{name}}, and the ones its body must keep. Filter by key, channel " +
            "or language. Requires notifications.templates.")]
        [ProducesResponseType(typeof(IReadOnlyList<MessageTemplateResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        public async Task<ActionResult<IReadOnlyList<MessageTemplateResponse>>> List([FromQuery] MessageTemplateFilterRequest filter, CancellationToken ct)
            => HandleResult(await service.ListAsync(filter, ct));

        [HttpPut("{templateId:int}")]
        [HasPermission(Permissions.Platform.NotificationTemplates)]
        [EndpointSummary("Change a message template")]
        [EndpointDescription(
            "Saves the text; messages sent from then on use it. The subject is the email's, and stays as it is when left " +
            "out. The text may only use the template's placeholders and its body must keep the required ones. A WhatsApp " +
            "template can't be edited, because Meta approves its text. The change is audited. Requires " +
            "notifications.templates.")]
        [ProducesResponseType(typeof(MessageTemplateResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        public async Task<ActionResult<MessageTemplateResponse>> Update(int templateId, MessageTemplateRequest request, CancellationToken ct)
            => HandleResult(await service.UpdateAsync(templateId, request, ct));

        [HttpPost("{templateId:int}/preview")]
        [HasPermission(Permissions.Platform.NotificationTemplates)]
        [EndpointSummary("Preview a message template")]
        [EndpointDescription(
            "The unsaved text with sample values in the template's language; nothing is saved. An email also comes as the " +
            "whole HTML message, an SMS with the number of parts it is sent as. Fails as saving the text would. Requires " +
            "notifications.templates.")]
        [ProducesResponseType(typeof(TemplatePreviewResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        public async Task<ActionResult<TemplatePreviewResponse>> Preview(int templateId, MessageTemplateRequest request, CancellationToken ct)
            => HandleResult(await service.PreviewAsync(templateId, request, ct));

        [HttpPost("{templateId:int}/reset")]
        [HasPermission(Permissions.Platform.NotificationTemplates)]
        [EndpointSummary("Reset a message template")]
        [EndpointDescription("Puts back the text the system ships. The change is audited. Requires notifications.templates.")]
        [ProducesResponseType(typeof(MessageTemplateResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<MessageTemplateResponse>> Reset(int templateId, CancellationToken ct)
            => HandleResult(await service.ResetAsync(templateId, ct));
    }
}
