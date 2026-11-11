using Domain.Enums;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Services.Messaging
{
    // An email as it is sent: the same message as plain text and as HTML
    public sealed record RenderedEmail(string Subject, string TextBody, string HtmlBody);

    // Turns an email template's plain-text body into the email. In the body, a blank line starts a paragraph and
    // [label](url) is a link, drawn as a button when it is alone in its paragraph. Links are read from the template
    // before its placeholders are filled, and every value is HTML-encoded, so a value (a patient's name) can never
    // add a link or markup. The frame around the body (header, footer, right-to-left for Arabic) is the same for
    // every message
    public static partial class EmailLayout
    {
        [GeneratedRegex(@"\[([^\[\]\n]+)\]\(([^()\s]+)\)")]
        private static partial Regex Link();

        [GeneratedRegex(@"\n[ \t]*(?:\n[ \t]*)+")]
        private static partial Regex BlankLines();

        public static RenderedEmail Render(
            string subject, string body, IReadOnlyDictionary<string, string> values, Language language, string clinicName, int year)
        {
            var filledSubject = TemplateSyntax.Fill(subject, values).ReplaceLineEndings(" ").Trim();
            var paragraphs = BlankLines().Split(body.ReplaceLineEndings("\n").Trim())
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .ToList();

            var text = string.Join("\n\n", paragraphs.Select(p => TextParagraph(p, values)));
            var html = Frame(
                title: Encode(filledSubject),
                preview: paragraphs.Count > 0 ? Encode(TextParagraph(paragraphs[0], values)) : "",
                bodyHtml: string.Concat(paragraphs.Select(p => HtmlParagraph(p, values, language))),
                language, Encode(clinicName), year);

            return new RenderedEmail(filledSubject, text, html);
        }

        // A link becomes "label: url"
        private static string TextParagraph(string paragraph, IReadOnlyDictionary<string, string> values)
            => TemplateSyntax.Fill(Link().Replace(paragraph, "$1: $2"), values);

        private static string HtmlParagraph(string paragraph, IReadOnlyDictionary<string, string> values, Language language)
        {
            var align = language == Language.Ar ? "right" : "left";
            var links = Link().Matches(paragraph);

            if (links.Count == 1 && links[0].Length == paragraph.Length
                && Url(links[0], values) is { } buttonUrl)
                return Button(Encode(TemplateSyntax.Fill(links[0].Groups[1].Value, values)), Encode(buttonUrl));

            var html = new StringBuilder();
            var position = 0;
            foreach (Match link in links)
            {
                html.Append(EncodeText(paragraph[position..link.Index], values));
                var label = Encode(TemplateSyntax.Fill(link.Groups[1].Value, values));
                html.Append(Url(link, values) is { } url
                    ? $"""<a href="{Encode(url)}" style="color:#0D9488;">{label}</a>"""
                    : $"{label}: {Encode(TemplateSyntax.Fill(link.Groups[2].Value, values))}");
                position = link.Index + link.Length;
            }
            html.Append(EncodeText(paragraph[position..], values));

            return $"""
                <p style="margin:0 0 20px;font-size:15px;line-height:1.7;color:#374151;text-align:{align};">{html}</p>

                """;
        }

        // The link's address once filled, or null unless it is an http or https URL: nothing else is made clickable
        private static string? Url(Match link, IReadOnlyDictionary<string, string> values)
        {
            var url = TemplateSyntax.Fill(link.Groups[2].Value, values);
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                ? url
                : null;
        }

        private static string EncodeText(string template, IReadOnlyDictionary<string, string> values)
            => Encode(TemplateSyntax.Fill(template, values)).Replace("\n", "<br/>\n");

        private static string Encode(string value) => WebUtility.HtmlEncode(value);

        private static string Button(string label, string url) => $"""
            <table cellpadding="0" cellspacing="0" border="0" style="margin:8px auto 28px;">
              <tr>
                <td align="center" style="background:#0D9488;border-radius:8px;">
                  <a href="{url}"
                     style="display:inline-block;padding:14px 32px;font-size:15px;font-weight:600;color:#ffffff;text-decoration:none;">
                    {label}
                  </a>
                </td>
              </tr>
            </table>

            """;

        // title, preview and clinicName are already encoded
        private static string Frame(string title, string preview, string bodyHtml, Language language, string clinicName, int year)
        {
            var (code, direction) = language == Language.Ar ? ("ar", "rtl") : ("en", "ltr");
            var (rights, reason) = language == Language.Ar
                ? ("جميع الحقوق محفوظة.", $"وصلتك هذه الرسالة لأن لديك حسابًا لدى {clinicName}.")
                : ("All rights reserved.", $"You received this email because you have an account with {clinicName}.");

            return $"""
                <!DOCTYPE html>
                <html lang="{code}" dir="{direction}">
                <head>
                  <meta charset="UTF-8" />
                  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
                  <title>{title}</title>
                </head>
                <body dir="{direction}" style="margin:0;padding:0;background-color:#F3F4F6;font-family:'Segoe UI',Tahoma,Arial,sans-serif;">
                  <span style="display:none;max-height:0;overflow:hidden;mso-hide:all;">{preview}</span>
                  <table width="100%" cellpadding="0" cellspacing="0" border="0" dir="{direction}" style="background:#F3F4F6;padding:40px 16px;">
                    <tr>
                      <td align="center">
                        <table width="600" cellpadding="0" cellspacing="0" border="0" dir="{direction}" style="max-width:600px;width:100%;">
                          <tr>
                            <td align="center" style="background:#0F766E;border-radius:12px 12px 0 0;padding:32px 40px;">
                              <span style="font-size:20px;font-weight:700;color:#ffffff;">&#9877; {clinicName}</span>
                              <p style="margin:16px 0 0;font-size:22px;font-weight:600;color:#ffffff;">{title}</p>
                            </td>
                          </tr>
                          <tr>
                            <td style="background:#ffffff;padding:40px;border-left:1px solid #E5E7EB;border-right:1px solid #E5E7EB;">
                {bodyHtml}
                            </td>
                          </tr>
                          <tr>
                            <td style="background:#F9FAFB;border:1px solid #E5E7EB;border-top:none;border-radius:0 0 12px 12px;padding:24px 40px;text-align:center;">
                              <p style="margin:0 0 6px;font-size:12px;color:#9CA3AF;">&copy; {year} {clinicName}. {rights}</p>
                              <p style="margin:0;font-size:12px;color:#9CA3AF;">{reason}</p>
                            </td>
                          </tr>
                        </table>
                      </td>
                    </tr>
                  </table>
                </body>
                </html>
                """;
        }
    }
}
