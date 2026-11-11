using Domain.Enums;
using NSubstitute;
using Shared.DTOs.Messaging;
using Shared.Errors;
using Shared.Messaging;

namespace ClinicFlow.Tests.Services.MessageTemplateServiceTests
{
    public class UpdateAsyncTests : MessageTemplateServiceTestsBase
    {
        private const string Body = "See you on {{date}} at {{time}}, {{patientName}}.";

        [Fact]
        public async Task UpdateAsync_SavesTheNewText_AndTheTemplateIsNoLongerTheDefault()
        {
            var template = StoredReminderEmail();

            var result = await _sut.UpdateAsync(1, new MessageTemplateRequest("Tomorrow: {{clinicName}}", Body), _ct);

            Assert.True(result.IsSuccess);
            Assert.Equal(("Tomorrow: {{clinicName}}", Body, false, Now), (template.Subject, template.Body, template.IsDefault, template.UpdatedAt));
            Assert.Equal((1, "Tomorrow: {{clinicName}}", Body, false, Now),
                (result.Value.Id, result.Value.Subject, result.Value.Body, result.Value.IsDefault, result.Value.UpdatedAt));
            await _uow.Received(1).SaveChangesAsync(_ct);
        }

        [Fact]
        public async Task UpdateAsync_WithoutASubject_KeepsTheEmailsSubject()
        {
            var template = StoredReminderEmail();
            var subject = template.Subject;

            var result = await _sut.UpdateAsync(1, new MessageTemplateRequest(null, Body), _ct);

            Assert.Equal((subject, Body), (result.Value.Subject, result.Value.Body));
        }

        [Fact]
        public async Task UpdateAsync_AnSms_HasNoSubjectEvenWhenOneIsSent()
        {
            var template = StoredReminderSms();

            var result = await _sut.UpdateAsync(2, new MessageTemplateRequest("Ignored", Body), _ct);

            Assert.True(result.IsSuccess);
            Assert.Null(template.Subject);
            Assert.Equal(Body, template.Body);
        }

        [Fact]
        public async Task UpdateAsync_StoresTheBodyTrimmedWithUnixLineEnds()
        {
            var template = StoredReminderEmail();

            await _sut.UpdateAsync(1, new MessageTemplateRequest("  Reminder  ", $"  Hello\r\n\r\n{Body}\r\n"), _ct);

            Assert.Equal(("Reminder", $"Hello\n\n{Body}"), (template.Subject, template.Body));
        }

        [Fact]
        public async Task UpdateAsync_TheTextItAlreadyHas_SavesNothing()
        {
            // No save, so no audit entry for a change that isn't one
            var template = StoredReminderEmail();

            var result = await _sut.UpdateAsync(1, new MessageTemplateRequest(template.Subject, template.Body.Replace("\n", "\r\n")), _ct);

            Assert.True(result.IsSuccess);
            Assert.Equal((true, SeededAt), (template.IsDefault, template.UpdatedAt));
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task UpdateAsync_BackToTheDefaultTextByHand_IsTheDefaultAgain()
        {
            var template = StoredReminderEmail();
            var (subject, body) = (template.Subject, template.Body);
            await _sut.UpdateAsync(1, new MessageTemplateRequest("Changed", Body), _ct);

            var result = await _sut.UpdateAsync(1, new MessageTemplateRequest(subject, body), _ct);

            Assert.True(result.Value.IsDefault);
        }

        [Fact]
        public async Task UpdateAsync_AnUnknownTemplate_IsNotFound()
        {
            var result = await _sut.UpdateAsync(404, new MessageTemplateRequest(null, Body), _ct);

            Assert.Equal(TemplateErrors.NotFound(404), Assert.Single(result.Errors));
        }

        [Fact]
        public async Task UpdateAsync_AWhatsAppTemplate_IsNotEditable()
        {
            var template = StoredReminderWhatsApp();
            var body = template.Body;

            var result = await _sut.UpdateAsync(3, new MessageTemplateRequest(null, Body), _ct);

            Assert.Equal(TemplateErrors.NotEditable, Assert.Single(result.Errors));
            Assert.Equal(body, template.Body);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task UpdateAsync_APlaceholderTheMessageDoesNotOffer_IsRejectedAndNothingIsSaved()
        {
            var template = StoredReminderEmail();
            var body = template.Body;

            var result = await _sut.UpdateAsync(1, new MessageTemplateRequest(null, Body + " {{resetUrl}}"), _ct);

            Assert.Equal(TemplateErrors.UnknownPlaceholder(TemplatePlaceholders.ResetUrl), Assert.Single(result.Errors));
            Assert.Equal(body, template.Body);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task UpdateAsync_AnUnknownPlaceholderInTheSubject_IsRejected()
        {
            StoredReminderEmail();

            var result = await _sut.UpdateAsync(1, new MessageTemplateRequest("For {{name}}", Body), _ct);

            Assert.Equal(TemplateErrors.UnknownPlaceholder(TemplatePlaceholders.Name), Assert.Single(result.Errors));
        }

        [Fact]
        public async Task UpdateAsync_ABodyWithoutARequiredPlaceholder_IsRejected()
        {
            StoredReminderEmail();

            var result = await _sut.UpdateAsync(1, new MessageTemplateRequest(null, "See you on {{date}}."), _ct);

            Assert.Equal(TemplateErrors.RequiredPlaceholderMissing(TemplatePlaceholders.Time), Assert.Single(result.Errors));
        }

        [Fact]
        public async Task UpdateAsync_AnArabicTemplate_IsCheckedTheSameWay()
        {
            var template = StoredReminderSms(Language.Ar);

            var result = await _sut.UpdateAsync(2, new MessageTemplateRequest(null, "موعدك يوم {{date}} الساعة {{time}}"), _ct);

            Assert.True(result.IsSuccess);
            Assert.Equal("موعدك يوم {{date}} الساعة {{time}}", template.Body);
        }
    }
}
