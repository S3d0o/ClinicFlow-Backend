using Services.Abstraction.Contracts;
using Shared.Messaging;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace ClinicFlow.IntegrationTests.Infrastructure
{
    public enum SentEmailKind { EmailConfirmation, PasswordReset, AppointmentConfirmation, AppointmentReminder, Other }

    // UserId and Token are read from the email's link, when it has one (the token as the link carries it, URL-escaped).
    // Message is the whole email as the API handed it to the sender
    public record SentEmail(SentEmailKind Kind, string To, Guid? UserId = null, string? Token = null, EmailMessage? Message = null);

    /// <summary>
    /// Replaces the SMTP EmailService in integration tests: nothing leaves the machine, and tests can
    /// read the confirmation and reset tokens from the recorded emails. Registered as a singleton
    /// shared by every test class; ResetDataBaseAsync calls <see cref="Clear"/>.
    /// </summary>
    public partial class FakeEmailService : IEmailService
    {
        private readonly ConcurrentQueue<SentEmail> _sent = new();
        private readonly ConcurrentDictionary<string, bool> _failingRecipients = new(StringComparer.OrdinalIgnoreCase);

        [GeneratedRegex(@"userId=([0-9a-fA-F-]{36})&token=(\S+)")]
        private static partial Regex Link();

        public IReadOnlyList<SentEmail> Sent => _sent.ToArray();

        // When true, every send throws, as an unreachable SMTP server would
        public bool FailSends { get; set; }

        // Sends to these addresses throw, as for a mailbox the SMTP server rejects
        public void FailFor(params string[] recipients)
        {
            foreach (var recipient in recipients)
                _failingRecipients[recipient] = true;
        }

        public void Clear()
        {
            _sent.Clear();
            FailSends = false;
            _failingRecipients.Clear();
        }

        public Task SendAsync(EmailMessage message, CancellationToken ct)
        {
            if (FailSends || _failingRecipients.ContainsKey(message.ToAddress))
                throw new InvalidOperationException($"SMTP unavailable for {message.ToAddress} (FakeEmailService)");

            var link = Link().Match(message.TextBody);
            _sent.Enqueue(new SentEmail(
                KindOf(message.TemplateKey),
                message.ToAddress,
                link.Success ? Guid.Parse(link.Groups[1].Value) : null,
                link.Success ? link.Groups[2].Value : null,
                message));
            return Task.CompletedTask;
        }

        private static SentEmailKind KindOf(string templateKey) => templateKey switch
        {
            TemplateKeys.EmailConfirmation => SentEmailKind.EmailConfirmation,
            TemplateKeys.PasswordReset => SentEmailKind.PasswordReset,
            TemplateKeys.AppointmentConfirmed => SentEmailKind.AppointmentConfirmation,
            TemplateKeys.AppointmentReminder => SentEmailKind.AppointmentReminder,
            _ => SentEmailKind.Other,
        };
    }
}
