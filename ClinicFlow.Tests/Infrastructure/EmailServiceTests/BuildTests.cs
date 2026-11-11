using Infrastructure.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;
using Services.Abstraction.Contracts;
using Shared.Settings;

namespace ClinicFlow.Tests.Infrastructure.EmailServiceTests
{
    // The adapter only delivers what it is given (P1 PR 5); the SMTP send itself isn't tested
    public class BuildTests
    {
        private static readonly EmailService Sut = new(
            Options.Create(new EmailSettings { FromName = "ClinicFlow", FromAddress = "clinic@example.com" }),
            NullLogger<EmailService>.Instance);

        private static readonly EmailMessage Email = new(
            "mona@test.com", "منى حسن", "تم تأكيد موعدك", "مرحبًا منى", "<html dir=\"rtl\"><body>مرحبًا منى</body></html>", "AppointmentConfirmed");

        [Fact]
        public void Build_AddressesTheMessageFromTheConfiguredSender()
        {
            using var message = Sut.Build(Email);

            Assert.Equal(("ClinicFlow", "clinic@example.com"), (message.From.Mailboxes.Single().Name, message.From.Mailboxes.Single().Address));
            Assert.Equal(("منى حسن", "mona@test.com"), (message.To.Mailboxes.Single().Name, message.To.Mailboxes.Single().Address));
            Assert.Equal("تم تأكيد موعدك", message.Subject);
        }

        [Fact]
        public void Build_CarriesTheTextAndTheHtmlAsAlternatives()
        {
            using var message = Sut.Build(Email);

            Assert.IsType<MultipartAlternative>(message.Body);
            Assert.Equal("مرحبًا منى", message.TextBody);
            Assert.Equal("<html dir=\"rtl\"><body>مرحبًا منى</body></html>", message.HtmlBody);
        }
    }
}
