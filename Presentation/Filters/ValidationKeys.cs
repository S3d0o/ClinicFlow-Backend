using System.Text.Json;

namespace Presentation.Filters
{
    // Validation error keys are the JSON names of the fields (P1 plan Q-F, contract §3.1 B and §13.1)
    public static class ValidationKeys
    {
        // A FluentValidation property path in the JSON naming policy, segment by segment:
        // "Email" → "email", "Lines[0].UnitPrice" → "lines[0].unitPrice", "SlotId" → "slotId". "" (a rule on the whole
        // request) stays "". Only property paths go through here; business-rule errors keep their code as the key (shape C)
        public static string ToCamelCase(string propertyPath)
        {
            if (string.IsNullOrEmpty(propertyPath))
                return propertyPath;

            var segments = propertyPath.Split('.');
            for (var i = 0; i < segments.Length; i++)
                segments[i] = JsonNamingPolicy.CamelCase.ConvertName(segments[i]);

            return string.Join('.', segments);
        }
    }
}
