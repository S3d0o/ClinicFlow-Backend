using Domain.Entities.Messaging;
using Domain.Enums;
using NSubstitute;
using Shared.DTOs.Messaging;
using Shared.Messaging;

namespace ClinicFlow.Tests.Services.MessageTemplateServiceTests
{
    public class ListAsyncTests : MessageTemplateServiceTestsBase
    {
        [Fact]
        public async Task ListAsync_ReturnsEachTemplateWithWhatItsMessageAllows()
        {
            var email = StoredReminderEmail();
            var whatsApp = StoredReminderWhatsApp();
            _uow.MessageTemplates.ListAsync(null, null, null, _ct).Returns([email, whatsApp]);

            var result = await _sut.ListAsync(new MessageTemplateFilterRequest(), _ct);

            Assert.True(result.IsSuccess);
            Assert.Equal(2, result.Value.Count);
            var first = result.Value[0];
            Assert.Equal((1, TemplateKeys.AppointmentReminder, TemplateChannel.Email, Language.En), (first.Id, first.Key, first.Channel, first.Language));
            Assert.Equal((email.Subject, email.Body, true, true, SeededAt), (first.Subject, first.Body, first.Editable, first.IsDefault, first.UpdatedAt));
            Assert.Equal(MessageTemplateCatalog.Find(TemplateKeys.AppointmentReminder)!.Placeholders, first.Placeholders);
            Assert.Equal([TemplatePlaceholders.Date, TemplatePlaceholders.Time], first.RequiredPlaceholders);
            Assert.False(result.Value[1].Editable); // Meta approves a WhatsApp text
            Assert.Null(result.Value[1].Subject);
        }

        [Fact]
        public async Task ListAsync_PassesTheFiltersToTheRepository()
        {
            _uow.MessageTemplates.ListAsync(TemplateKeys.PasswordReset, TemplateChannel.Email, Language.Ar, _ct).Returns([]);

            var result = await _sut.ListAsync(new MessageTemplateFilterRequest(TemplateKeys.PasswordReset, TemplateChannel.Email, Language.Ar), _ct);

            Assert.Empty(result.Value);
            await _uow.MessageTemplates.Received(1).ListAsync(TemplateKeys.PasswordReset, TemplateChannel.Email, Language.Ar, _ct);
        }

        [Fact]
        public async Task ListAsync_ARowWhoseMessageTheCatalogDropped_IsLeftOut()
        {
            var retired = new MessageTemplate { Id = 9, Key = "RetiredMessage", Channel = TemplateChannel.Email, Language = Language.En, Body = "x" };
            var current = StoredReminderEmail();
            _uow.MessageTemplates.ListAsync(null, null, null, _ct).Returns([retired, current]);

            var result = await _sut.ListAsync(new MessageTemplateFilterRequest(), _ct);

            Assert.Equal(1, Assert.Single(result.Value).Id);
        }
    }
}
