using Domain.Enums;
using Services.Messaging;
using Shared.Messaging;

namespace ClinicFlow.Tests.Services.Messaging
{
    // The catalog is what every clinic starts with, so its own texts must pass the rules an edit has to pass
    public class MessageTemplateCatalogTests
    {
        public static TheoryData<string, TemplateChannel, Language> Defaults()
        {
            var data = new TheoryData<string, TemplateChannel, Language>();
            foreach (var definition in MessageTemplateCatalog.All)
                foreach (var text in definition.Defaults)
                    data.Add(definition.Key, text.Channel, text.Language);
            return data;
        }

        private static (TemplateDefinition Definition, TemplateDefault Text) Find(string key, TemplateChannel channel, Language language)
        {
            var definition = MessageTemplateCatalog.Find(key)!;
            return (definition, definition.DefaultFor(channel, language)!);
        }

        [Theory]
        [MemberData(nameof(Defaults))]
        public void EveryDefaultText_IsValidForItsMessage(string key, TemplateChannel channel, Language language)
        {
            var (definition, text) = Find(key, channel, language);

            Assert.Null(TemplateSyntax.Validate(definition, text.Subject, text.Body));
        }

        [Theory]
        [MemberData(nameof(Defaults))]
        public void EveryDefaultText_RendersWithTheSampleValues(string key, TemplateChannel channel, Language language)
        {
            var (_, text) = Find(key, channel, language);
            var samples = MessageTemplateCatalog.Samples(language);

            Assert.DoesNotContain("{{", TemplateSyntax.Fill(text.Body, samples));
            if (text.Subject is not null)
                Assert.DoesNotContain("{{", TemplateSyntax.Fill(text.Subject, samples));
        }

        [Theory]
        [MemberData(nameof(Defaults))]
        public void EveryDefaultText_HasASubjectOnlyByEmail_AMetaNameOnlyByWhatsApp_AndUnixLineEnds(string key, TemplateChannel channel, Language language)
        {
            var (_, text) = Find(key, channel, language);

            Assert.Equal(channel == TemplateChannel.Email, text.Subject is not null);
            Assert.Equal(channel == TemplateChannel.WhatsApp, text.ProviderTemplateName is not null);
            Assert.DoesNotContain('\r', text.Body);
        }

        [Fact]
        public void EveryMessage_HasEachOfItsChannelsInBothLanguages()
        {
            foreach (var definition in MessageTemplateCatalog.All)
                foreach (var channel in definition.Defaults.Select(d => d.Channel).Distinct())
                    Assert.Equal([Language.Ar, Language.En], definition.Defaults.Where(d => d.Channel == channel).Select(d => d.Language).Order());
        }

        [Fact]
        public void EveryMessage_OffersItsRequiredPlaceholders_AndTheClinicName()
        {
            foreach (var definition in MessageTemplateCatalog.All)
            {
                Assert.Subset(definition.Placeholders.ToHashSet(), definition.RequiredPlaceholders.ToHashSet());
                Assert.Contains(TemplatePlaceholders.ClinicName, definition.Placeholders);
            }
        }

        [Fact]
        public void AppointmentMessages_GoByEmailSmsAndWhatsApp_AccountMessagesByEmailOnly()
        {
            TemplateChannel[] Channels(string key) => [.. MessageTemplateCatalog.Find(key)!.Defaults.Select(d => d.Channel).Distinct().Order()];

            Assert.Equal([TemplateChannel.Email, TemplateChannel.WhatsApp, TemplateChannel.Sms], Channels(TemplateKeys.AppointmentConfirmed));
            Assert.Equal([TemplateChannel.Email, TemplateChannel.WhatsApp, TemplateChannel.Sms], Channels(TemplateKeys.AppointmentReminder));
            Assert.Equal([TemplateChannel.Email], Channels(TemplateKeys.EmailConfirmation));
            Assert.Equal([TemplateChannel.Email], Channels(TemplateKeys.PasswordReset));
        }

        [Fact]
        public void ArabicSmsDefaults_WithSampleValues_FitInThreeParts()
        {
            // A reminder that costs more than a few SMS each time would surprise the clinic
            foreach (var text in MessageTemplateCatalog.All.SelectMany(d => d.Defaults).Where(t => t.Channel == TemplateChannel.Sms))
                Assert.InRange(SmsSegments.Count(TemplateSyntax.Fill(text.Body, MessageTemplateCatalog.Samples(text.Language))), 1, 3);
        }

        [Fact]
        public void Find_AnUnknownKey_IsNull()
            => Assert.Null(MessageTemplateCatalog.Find("Nope"));
    }
}
