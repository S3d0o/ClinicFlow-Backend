using Domain.Enums;
using NSubstitute;
using Shared.DTOs.Messaging;
using Shared.Errors;
using Shared.Messaging;

namespace ClinicFlow.Tests.Services.MessageTemplateServiceTests
{
    public class PreviewAsyncTests : MessageTemplateServiceTestsBase
    {
        [Fact]
        public async Task PreviewAsync_AnEmail_ReturnsTheTextAndTheWholeHtmlWithSampleValues_AndSavesNothing()
        {
            var template = StoredReminderEmail();
            var stored = template.Body;

            var result = await _sut.PreviewAsync(1, new MessageTemplateRequest("For {{patientName}}", "On {{date}} at {{time}}\n\nBye"), _ct);

            Assert.True(result.IsSuccess);
            Assert.Equal(("For Mona Hassan", "On Sunday, November 15, 2026 at 10:30 AM\n\nBye"), (result.Value.Subject, result.Value.Body));
            Assert.Contains("<html lang=\"en\" dir=\"ltr\">", result.Value.Html);
            Assert.Contains("Al Shifa Clinic", result.Value.Html); // the sample clinic in the frame
            Assert.Null(result.Value.SmsSegments);
            Assert.Equal(stored, template.Body);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task PreviewAsync_AnArabicEmail_UsesArabicSamplesRightToLeft()
        {
            StoredReminderEmail(Language.Ar);

            var result = await _sut.PreviewAsync(1, new MessageTemplateRequest(null, "{{patientName}}: {{date}} {{time}}"), _ct);

            Assert.Equal("منى حسن: الأحد، 15 نوفمبر 2026 10:30 ص", result.Value.Body);
            Assert.Equal("تذكير بموعدك يوم الأحد، 15 نوفمبر 2026", result.Value.Subject); // the stored subject, filled
            Assert.Contains("dir=\"rtl\"", result.Value.Html);
        }

        [Fact]
        public async Task PreviewAsync_AnSms_ReturnsTheTextAndHowManyPartsItTakes()
        {
            StoredReminderSms(Language.Ar);
            var body = "{{date}} {{time}} " + new string('ع', 60);

            var result = await _sut.PreviewAsync(2, new MessageTemplateRequest(null, body), _ct);

            // 21 + 1 + 7 + 1 + 60 = 90 Arabic characters: two parts of 67
            Assert.Equal("الأحد، 15 نوفمبر 2026 10:30 ص " + new string('ع', 60), result.Value.Body);
            Assert.Equal(2, result.Value.SmsSegments);
            Assert.Null(result.Value.Subject);
            Assert.Null(result.Value.Html);
        }

        [Fact]
        public async Task PreviewAsync_FailsAsTheSaveWould()
        {
            StoredReminderEmail();
            StoredReminderWhatsApp();

            Assert.Equal(TemplateErrors.NotFound(404), Assert.Single((await _sut.PreviewAsync(404, new MessageTemplateRequest(null, "x"), _ct)).Errors));
            Assert.Equal(TemplateErrors.NotEditable, Assert.Single((await _sut.PreviewAsync(3, new MessageTemplateRequest(null, "{{date}} {{time}}"), _ct)).Errors));
            Assert.Equal(TemplateErrors.UnknownPlaceholder("x"),
                Assert.Single((await _sut.PreviewAsync(1, new MessageTemplateRequest(null, "{{date}} {{time}} {{x}}"), _ct)).Errors));
            Assert.Equal(TemplateErrors.RequiredPlaceholderMissing(TemplatePlaceholders.Date),
                Assert.Single((await _sut.PreviewAsync(1, new MessageTemplateRequest(null, "{{time}}"), _ct)).Errors));
        }
    }
}
