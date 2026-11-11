using Domain.Entities.Messaging;
using Domain.Enums;
using Domain.Interfaces;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Services.Abstraction.Contracts;
using Services.Implementations;
using Shared.Messaging;

namespace ClinicFlow.Tests.Services.MessageTemplateServiceTests
{
    public abstract class MessageTemplateServiceTestsBase
    {
        protected static readonly DateTime SeededAt = new(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc);
        protected static readonly DateTime Now = new(2026, 11, 11, 9, 0, 0, DateTimeKind.Utc);
        protected readonly CancellationToken _ct = CancellationToken.None;

        protected readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
        protected readonly IClock _clock = Substitute.For<IClock>();
        protected readonly ILogger<MessageTemplateService> _logger = Substitute.For<ILogger<MessageTemplateService>>();
        protected readonly MessageTemplateService _sut;

        protected MessageTemplateServiceTestsBase()
        {
            _clock.UtcNow.Returns(Now);
            _sut = new MessageTemplateService(_uow, _clock, _logger);
        }

        // A row as startup creates it, with the catalog's default text, found by its id
        protected MessageTemplate Stored(int id, string key, TemplateChannel channel, Language language)
        {
            var shipped = MessageTemplateCatalog.Find(key)!.DefaultFor(channel, language)!;
            var template = new MessageTemplate { Id = id, Key = key, Channel = channel, Language = language, ProviderTemplateName = shipped.ProviderTemplateName };
            template.SetText(shipped.Subject, shipped.Body, isDefault: true, SeededAt);
            _uow.MessageTemplates.GetByIdAsync(id, _ct).Returns(template);
            return template;
        }

        protected MessageTemplate StoredReminderEmail(Language language = Language.En)
            => Stored(1, TemplateKeys.AppointmentReminder, TemplateChannel.Email, language);

        protected MessageTemplate StoredReminderSms(Language language = Language.Ar)
            => Stored(2, TemplateKeys.AppointmentReminder, TemplateChannel.Sms, language);

        protected MessageTemplate StoredReminderWhatsApp()
            => Stored(3, TemplateKeys.AppointmentReminder, TemplateChannel.WhatsApp, Language.Ar);
    }
}
