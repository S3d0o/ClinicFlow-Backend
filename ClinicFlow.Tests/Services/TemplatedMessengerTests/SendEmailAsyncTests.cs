using Domain.Entities.IdentityModule;
using Domain.Entities.Messaging;
using Domain.Entities.Tenancy;
using Domain.Enums;
using Domain.Interfaces;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Services.Abstraction.Contracts;
using Services.Implementations;
using Shared.Messaging;

namespace ClinicFlow.Tests.Services.TemplatedMessengerTests
{
    // A message is written from the organization's template in the recipient's language (FR-NTF-02, R10)
    public class SendEmailAsyncTests
    {
        private static readonly DateTime Now = new(2026, 11, 11, 9, 0, 0, DateTimeKind.Utc);
        private readonly CancellationToken _ct = CancellationToken.None;

        private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
        private readonly IEmailService _emailService = Substitute.For<IEmailService>();
        private readonly IClock _clock = Substitute.For<IClock>();
        private readonly Organization _organization = new() { LegalName = "Shifa LLC", BrandNameEn = "Al Shifa", BrandNameAr = "الشفاء" };
        private readonly ApplicationUser _user = new() { FirstName = "Mona", LastName = "Hassan", Email = "mona@test.com", PreferredLanguage = Language.En };
        private readonly Dictionary<string, string> _values = new()
        {
            [TemplatePlaceholders.Name] = "Mona Hassan",
            [TemplatePlaceholders.ConfirmUrl] = "https://app.test/#/confirm-email?userId=1&token=t",
        };
        private readonly TemplatedMessenger _sut;
        private EmailMessage? _sent;

        public SendEmailAsyncTests()
        {
            _clock.UtcNow.Returns(Now);
            _uow.Organizations.GetCurrentAsync(_ct).Returns(_organization);
            _emailService.SendAsync(Arg.Do<EmailMessage>(m => _sent = m), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
            _sut = new TemplatedMessenger(_uow, _emailService, _clock, Substitute.For<ILogger<TemplatedMessenger>>());
        }

        private void Stored(Language language, string? subject, string body)
            => _uow.MessageTemplates.FindAsync(TemplateKeys.EmailConfirmation, TemplateChannel.Email, language, _ct)
                .Returns(new MessageTemplate
                {
                    Id = 5, Key = TemplateKeys.EmailConfirmation, Channel = TemplateChannel.Email, Language = language, Subject = subject, Body = body,
                });

        private Task Send() => _sut.SendEmailAsync(_user, TemplateKeys.EmailConfirmation, _values, _ct);

        [Fact]
        public async Task SendEmailAsync_WritesTheOrganizationsTemplate_WithTheValuesAndTheClinicName()
        {
            Stored(Language.En, "Welcome to {{clinicName}}", "Hi {{name}}\n\n[Confirm]({{confirmUrl}})");

            await Send();

            Assert.NotNull(_sent);
            Assert.Equal(("mona@test.com", "Mona Hassan", "Welcome to Al Shifa", TemplateKeys.EmailConfirmation),
                (_sent.ToAddress, _sent.ToName, _sent.Subject, _sent.TemplateKey));
            Assert.Equal("Hi Mona Hassan\n\nConfirm: https://app.test/#/confirm-email?userId=1&token=t", _sent.TextBody);
            Assert.Contains("<html lang=\"en\" dir=\"ltr\">", _sent.HtmlBody);
            Assert.Contains("&copy; 2026 Al Shifa.", _sent.HtmlBody);
        }

        [Fact]
        public async Task SendEmailAsync_AUserWhoReadsArabic_GetsTheArabicTemplateRightToLeft()
        {
            _user.PreferredLanguage = Language.Ar;
            Stored(Language.Ar, "أهلًا بك في {{clinicName}}", "مرحبًا {{name}}\n\n[تأكيد]({{confirmUrl}})");
            Stored(Language.En, "Welcome", "Hi {{name}} {{confirmUrl}}");

            await Send();

            Assert.Equal("أهلًا بك في الشفاء", _sent!.Subject);
            Assert.StartsWith("مرحبًا Mona Hassan", _sent.TextBody);
            Assert.Contains("<html lang=\"ar\" dir=\"rtl\">", _sent.HtmlBody);
        }

        [Theory]
        [InlineData(Language.En, "Confirm your email address")]
        [InlineData(Language.Ar, "تأكيد بريدك الإلكتروني")]
        public async Task SendEmailAsync_WithoutARow_SendsTheCatalogsDefaultInTheUsersLanguage(Language language, string subject)
        {
            _user.PreferredLanguage = language;

            await Send();

            Assert.Equal(subject, _sent!.Subject);
            Assert.Contains("https://app.test/#/confirm-email?userId=1&token=t", _sent.TextBody);
        }

        [Fact]
        public async Task SendEmailAsync_ARowThatNoLongerFitsTheMessage_SendsTheDefaultInstead()
        {
            // e.g. a placeholder a later version stopped offering: the message must still go out
            Stored(Language.En, "Hello", "Hi {{nickname}} [Confirm]({{confirmUrl}})");

            await Send();

            Assert.Equal("Confirm your email address", _sent!.Subject);
        }

        [Fact]
        public async Task SendEmailAsync_ARowWithoutASubject_UsesTheDefaultSubject()
        {
            Stored(Language.En, null, "Go: {{confirmUrl}}");

            await Send();

            Assert.Equal(("Confirm your email address", "Go: https://app.test/#/confirm-email?userId=1&token=t"), (_sent!.Subject, _sent.TextBody));
        }

        [Theory]
        [InlineData("", "الشفاء", "Shifa LLC", "الشفاء")]    // no English brand: the Arabic one
        [InlineData("", "", "Shifa LLC", "Shifa LLC")]       // no brand at all: the legal name
        [InlineData("", "", "", "ClinicFlow")]
        public async Task SendEmailAsync_WithoutABrandNameInTheLanguage_FallsBack(string brandEn, string brandAr, string legal, string expected)
        {
            (_organization.BrandNameEn, _organization.BrandNameAr, _organization.LegalName) = (brandEn, brandAr, legal);
            Stored(Language.En, "{{clinicName}}", "{{confirmUrl}}");

            await Send();

            Assert.Equal(expected, _sent!.Subject);
        }

        [Fact]
        public async Task SendEmailAsync_AMissingValue_ThrowsAndSendsNothing()
        {
            _values.Remove(TemplatePlaceholders.Name);

            await Assert.ThrowsAsync<InvalidOperationException>(Send);

            await _emailService.DidNotReceiveWithAnyArgs().SendAsync(default!, default);
        }

        [Fact]
        public async Task SendEmailAsync_AnUnknownTemplate_Throws()
            => await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.SendEmailAsync(_user, "Nope", _values, _ct));

        [Fact]
        public async Task SendEmailAsync_WhenTheSenderFails_LetsItThrow()
        {
            // The outbox retries a handler that throws
            _emailService.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(new InvalidOperationException("SMTP down")));

            await Assert.ThrowsAsync<InvalidOperationException>(Send);
        }
    }
}
