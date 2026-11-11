using Domain.Enums;
using Services.Messaging;

namespace ClinicFlow.Tests.Services.Messaging
{
    // An email template's plain-text body becomes a text part and an HTML part (FR-NTF-02, R10)
    public class EmailLayoutTests
    {
        private static readonly Dictionary<string, string> Values = new()
        {
            ["name"] = "Mona",
            ["url"] = "https://app.test/#/confirm-email?userId=1&token=a%2Bb",
        };

        private static RenderedEmail Render(string body, Language language = Language.En, string subject = "Welcome {{name}}")
            => EmailLayout.Render(subject, body, Values, language, "Al Shifa", 2026);

        [Fact]
        public void Render_FillsTheSubject()
            => Assert.Equal("Welcome Mona", Render("Hi").Subject);

        [Fact]
        public void Render_TextBody_KeepsParagraphsAndWritesALinkAsLabelAndAddress()
        {
            var email = Render("Hello {{name}},\n\n\n[Confirm]({{url}})\r\n\r\nBye");

            Assert.Equal("Hello Mona,\n\nConfirm: https://app.test/#/confirm-email?userId=1&token=a%2Bb\n\nBye", email.TextBody);
        }

        [Fact]
        public void Render_HtmlBody_MakesAParagraphOfEachBlock_AndLineBreaksInside()
        {
            var html = Render("Doctor: A\nDate: B\n\nSee you").HtmlBody;

            Assert.Contains(">Doctor: A<br/>\nDate: B</p>", html);
            Assert.Contains(">See you</p>", html);
        }

        [Fact]
        public void Render_ALinkAloneInItsParagraph_IsAButtonToTheEncodedAddress()
        {
            var html = Render("Hello\n\n[Confirm email]({{url}})").HtmlBody;

            Assert.Contains("href=\"https://app.test/#/confirm-email?userId=1&amp;token=a%2Bb\"", html);
            Assert.Contains("Confirm email", html);
            Assert.Contains("border-radius:8px", html); // the button, not an inline link
        }

        [Fact]
        public void Render_ALinkInsideAParagraph_IsAnInlineLink()
        {
            var html = Render("Open [your account]({{url}}) to see it.").HtmlBody;

            Assert.Contains(">Open <a href=\"https://app.test/#/confirm-email?userId=1&amp;token=a%2Bb\" style=\"color:#0D9488;\">your account</a> to see it.</p>", html);
        }

        [Theory]
        [InlineData("javascript:alert(1)")]
        [InlineData("/relative/path")]
        [InlineData("file:///etc/passwd")]
        public void Render_ALinkThatIsNotHttp_IsNotClickable(string address)
        {
            var html = EmailLayout.Render("S", "[Open]({{url}})", new Dictionary<string, string> { ["url"] = address }, Language.En, "Clinic", 2026).HtmlBody;

            Assert.DoesNotContain("href=", html);
            Assert.Contains($"Open: {address}", html);
        }

        [Fact]
        public void Render_Values_AreEncoded_AndCannotAddLinksOrMarkup()
        {
            // A patient's name is theirs to choose: it must stay text
            var values = new Dictionary<string, string> { ["name"] = "<b>Mona</b> [free](https://evil.test)" };

            var email = EmailLayout.Render("Hi {{name}}", "Dear {{name}}", values, Language.En, "<Clinic>", 2026);

            Assert.Contains("Dear &lt;b&gt;Mona&lt;/b&gt; [free](https://evil.test)</p>", email.HtmlBody);
            Assert.DoesNotContain("href=", email.HtmlBody);
            Assert.DoesNotContain("<b>Mona", email.HtmlBody);
            Assert.Contains("&lt;Clinic&gt;", email.HtmlBody);
            Assert.DoesNotContain("<Clinic>", email.HtmlBody);
        }

        [Fact]
        public void Render_Arabic_IsRightToLeftWithArabicFooter()
        {
            var html = Render("مرحبًا {{name}}", Language.Ar, subject: "تأكيد").HtmlBody;

            Assert.Contains("<html lang=\"ar\" dir=\"rtl\">", html);
            Assert.Contains("text-align:right", html);
            Assert.Contains("مرحبًا Mona", html); // Arabic stays readable in the source, not numeric entities
            Assert.Contains("جميع الحقوق محفوظة", html);
        }

        [Fact]
        public void Render_English_IsLeftToRight_WithTheClinicAndTheYearInTheFrame()
        {
            var html = Render("Hello").HtmlBody;

            Assert.Contains("<html lang=\"en\" dir=\"ltr\">", html);
            Assert.Contains("text-align:left", html);
            Assert.Contains("&copy; 2026 Al Shifa. All rights reserved.", html);
            Assert.Contains("<title>Welcome Mona</title>", html);
        }
    }
}
