using Domain.Entities.Messaging;
using Domain.Enums;
using NSubstitute;
using Shared.DTOs.Messaging;
using Shared.Errors;
using Shared.Messaging;

namespace ClinicFlow.Tests.Services.MessageTemplateServiceTests
{
    public class ResetAsyncTests : MessageTemplateServiceTestsBase
    {
        [Fact]
        public async Task ResetAsync_AnEditedTemplate_GetsTheDefaultTextBack()
        {
            var template = StoredReminderEmail();
            var (subject, body) = (template.Subject, template.Body);
            await _sut.UpdateAsync(1, new MessageTemplateRequest("Changed", "{{date}} {{time}}"), _ct);
            _uow.ClearReceivedCalls();

            var result = await _sut.ResetAsync(1, _ct);

            Assert.True(result.IsSuccess);
            Assert.Equal((subject, body, true, Now), (template.Subject, template.Body, template.IsDefault, template.UpdatedAt));
            Assert.Equal((subject, body, true), (result.Value.Subject, result.Value.Body, result.Value.IsDefault));
            await _uow.Received(1).SaveChangesAsync(_ct);
        }

        [Fact]
        public async Task ResetAsync_ATemplateStillOnItsDefault_SavesNothing()
        {
            var template = StoredReminderEmail();

            var result = await _sut.ResetAsync(1, _ct);

            Assert.True(result.Value.IsDefault);
            Assert.Equal(SeededAt, template.UpdatedAt);
            await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ResetAsync_AnUnknownTemplate_IsNotFound()
        {
            var result = await _sut.ResetAsync(404, _ct);

            Assert.Equal(TemplateErrors.NotFound(404), Assert.Single(result.Errors));
        }

        [Fact]
        public async Task ResetAsync_ARowWhoseMessageTheCatalogDropped_IsNotFound()
        {
            _uow.MessageTemplates.GetByIdAsync(9, _ct)
                .Returns(new MessageTemplate { Id = 9, Key = "RetiredMessage", Channel = TemplateChannel.Email, Language = Language.En, Body = "x" });

            var result = await _sut.ResetAsync(9, _ct);

            Assert.Equal(TemplateErrors.NotFound(9), Assert.Single(result.Errors));
        }
    }
}
