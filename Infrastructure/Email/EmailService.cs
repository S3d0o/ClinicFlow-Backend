using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Shared.Settings;

namespace Infrastructure.Email;

// Delivers an email over SMTP. What it says comes from a message template, already written in the recipient's
// language (Services' TemplatedMessenger); to change the mail provider, only this class changes
public class EmailService(
    IOptions<EmailSettings> emailOptions,
    ILogger<EmailService> logger) : IEmailService
{
    // An unreachable SMTP server must not hold a request for MailKit's default 2 minutes per operation
    private const int SmtpTimeoutMilliseconds = 30_000;

    private readonly EmailSettings _settings = emailOptions.Value;

    public async Task SendAsync(EmailMessage email, CancellationToken ct)
    {
        using var message = Build(email);
        using var client = new SmtpClient { Timeout = SmtpTimeoutMilliseconds };

        await client.ConnectAsync(
            _settings.Host,
            _settings.Port,
            SecureSocketOptions.StartTls,   // port 587 — change to SslOnConnect for port 465
            ct);

        await client.AuthenticateAsync(
            _settings.Username,
            _settings.Password,
            ct);

        await client.SendAsync(message, ct);
        await client.DisconnectAsync(quit: true, ct);

        logger.LogInformation("{TemplateKey} email sent to {Email}", email.TemplateKey, email.ToAddress);
    }

    // Text and HTML as alternatives of one message, so a client shows whichever it can
    internal MimeMessage Build(EmailMessage email)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_settings.FromName, _settings.FromAddress));
        message.To.Add(new MailboxAddress(email.ToName, email.ToAddress));
        message.Subject = email.Subject;
        message.Body = new BodyBuilder { TextBody = email.TextBody, HtmlBody = email.HtmlBody }.ToMessageBody();
        return message;
    }
}
