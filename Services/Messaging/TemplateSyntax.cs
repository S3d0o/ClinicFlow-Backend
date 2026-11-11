using Shared.Messaging;
using System.Text.RegularExpressions;

namespace Services.Messaging
{
    // Placeholders in a message template: {{name}} (FR-NTF-02). A text may only use the names its message offers
    // (MessageTemplateCatalog), and a body must keep the required ones
    public static partial class TemplateSyntax
    {
        [GeneratedRegex(@"\{\{\s*([^{}]*?)\s*\}\}")]
        private static partial Regex Placeholder();

        public static IReadOnlyList<string> PlaceholdersIn(string? text)
            => text is null ? [] : Placeholder().Matches(text).Select(m => m.Groups[1].Value).Distinct().ToList();

        // Null when the texts are valid for the message
        public static Error? Validate(TemplateDefinition definition, string? subject, string body)
        {
            foreach (var text in new[] { subject, body })
            {
                if (text is null)
                    continue;

                if (PlaceholdersIn(text).FirstOrDefault(name => !definition.Placeholders.Contains(name)) is { } unknown)
                    return TemplateErrors.UnknownPlaceholder(unknown);

                // What is left of "{{name" or "name}}" once the well-formed placeholders are taken out
                var rest = Placeholder().Replace(text, " ");
                if (rest.Contains("{{") || rest.Contains("}}"))
                    return TemplateErrors.UnclosedPlaceholder;
            }

            var inBody = PlaceholdersIn(body);
            if (definition.RequiredPlaceholders.FirstOrDefault(name => !inBody.Contains(name)) is { } missing)
                return TemplateErrors.RequiredPlaceholderMissing(missing);

            return null;
        }

        // The text with each placeholder replaced by its value. A placeholder without one is a bug in whoever sends
        // the message, so it throws rather than send a message with a hole in it
        public static string Fill(string text, IReadOnlyDictionary<string, string> values)
            => Placeholder().Replace(text, match => values.TryGetValue(match.Groups[1].Value, out var value)
                ? value
                : throw new InvalidOperationException($"No value was given for the placeholder '{match.Groups[1].Value}'."));
    }
}
