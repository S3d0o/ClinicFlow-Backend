using Domain.ResultPattern;

namespace Shared.Errors
{
    // Message templates (FR-NTF-02; API_CONTRACT.md §13.28). The placeholder errors name it in a `placeholder` member
    public static class TemplateErrors
    {
        public const string PlaceholderMetadata = "placeholder";

        public static Error NotFound(int id) =>
            Error.NotFound("Template.NotFound", $"Message template with id {id} was not found");

        // Meta approves a WhatsApp text, so changing it here would only make it differ from what is sent
        public static readonly Error NotEditable =
            Error.Conflict("Template.NotEditable", "WhatsApp templates are approved by Meta and can't be edited here");

        public static Error UnknownPlaceholder(string name) =>
            Error.Validation("Template.UnknownPlaceholder", $"This message has no placeholder named '{name}'")
                .WithMetadata(PlaceholderMetadata, name);

        // "{{" without "}}", or the other way round
        public static readonly Error UnclosedPlaceholder =
            Error.Validation("Template.UnknownPlaceholder", "A placeholder must be written {{name}}: the text has {{ or }} on its own")
                .WithMetadata(PlaceholderMetadata, "{{");

        public static Error RequiredPlaceholderMissing(string name) =>
            Error.Validation("Template.RequiredPlaceholderMissing", $"The body must keep the placeholder '{name}'")
                .WithMetadata(PlaceholderMetadata, name);
    }
}
