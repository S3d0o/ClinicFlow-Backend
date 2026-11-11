using Services.Messaging;
using Shared.Errors;
using Shared.Messaging;

namespace ClinicFlow.Tests.Services.Messaging
{
    // {{name}} placeholders (FR-NTF-02): what a template's text may use, and how it is filled
    public class TemplateSyntaxTests
    {
        private static readonly TemplateDefinition Definition = new(
            "Test", placeholders: ["name", "link", "date"], requiredPlaceholders: ["link"], defaults: []);

        [Fact]
        public void PlaceholdersIn_ReturnsEachNameOnce_IgnoringSpacesInsideTheBraces()
            => Assert.Equal(["name", "link"], TemplateSyntax.PlaceholdersIn("Hi {{name}}, {{ link }} and {{name}} again"));

        [Fact]
        public void Validate_TextUsingOnlyItsPlaceholders_IsValid()
            => Assert.Null(TemplateSyntax.Validate(Definition, "For {{name}}", "Open {{link}} before {{date}}"));

        [Theory]
        [InlineData("Hello {{nmae}}", "Open {{link}}", "nmae")] // in the subject
        [InlineData("Hello", "Open {{link}} at {{clinic}}", "clinic")] // in the body
        [InlineData("Hello", "Open {{link}} {{Name}}", "Name")] // names are case-sensitive
        [InlineData("Hello", "Open {{link}} {{}}", "")]
        public void Validate_APlaceholderTheMessageDoesNotOffer_IsUnknown(string subject, string body, string placeholder)
            => Assert.Equal(TemplateErrors.UnknownPlaceholder(placeholder), TemplateSyntax.Validate(Definition, subject, body));

        [Theory]
        [InlineData("Open {{link}} for {{name")]
        [InlineData("Open {{link}} for name}}")]
        [InlineData("Open {{link}} {{ {{name}}")]
        public void Validate_BracesThatDoNotClose_AreRejected(string body)
            => Assert.Equal(TemplateErrors.UnclosedPlaceholder, TemplateSyntax.Validate(Definition, null, body));

        [Fact]
        public void Validate_SingleBraces_AreOrdinaryText()
            => Assert.Null(TemplateSyntax.Validate(Definition, null, "Open {{link}} {see below}"));

        [Fact]
        public void Validate_ABodyWithoutARequiredPlaceholder_IsRejected()
            => Assert.Equal(TemplateErrors.RequiredPlaceholderMissing("link"), TemplateSyntax.Validate(Definition, "{{link}}", "Hello {{name}}"));

        [Fact]
        public void Validate_WithoutASubject_ChecksTheBodyOnly()
            => Assert.Null(TemplateSyntax.Validate(Definition, null, "{{link}}"));

        [Fact]
        public void PlaceholderErrors_NameThePlaceholder()
        {
            Assert.Equal("date", TemplateErrors.UnknownPlaceholder("date").Metadata[TemplateErrors.PlaceholderMetadata]);
            Assert.Equal("link", TemplateErrors.RequiredPlaceholderMissing("link").Metadata[TemplateErrors.PlaceholderMetadata]);
        }

        [Fact]
        public void Fill_ReplacesEveryPlaceholderWithItsValue()
            => Assert.Equal(
                "Hi Mona, see https://x.test. Bye Mona",
                TemplateSyntax.Fill("Hi {{name}}, see {{ link }}. Bye {{name}}", new Dictionary<string, string> { ["name"] = "Mona", ["link"] = "https://x.test" }));

        [Fact]
        public void Fill_AValueThatLooksLikeAPlaceholder_IsNotFilledAgain()
            => Assert.Equal("Hi {{link}}", TemplateSyntax.Fill("Hi {{name}}", new Dictionary<string, string> { ["name"] = "{{link}}", ["link"] = "x" }));

        [Fact]
        public void Fill_APlaceholderWithoutAValue_Throws()
        {
            var error = Assert.Throws<InvalidOperationException>(() => TemplateSyntax.Fill("Hi {{name}}", new Dictionary<string, string>()));
            Assert.Contains("name", error.Message);
        }
    }
}
