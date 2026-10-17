using Services.Abstraction.Contracts;
using System.Collections.Concurrent;

namespace ClinicFlow.IntegrationTests.Infrastructure
{
    public enum SentEmailKind { EmailConfirmation, PasswordReset, AppointmentConfirmation, AppointmentReminder }

    // Token is the value placed in the email link (already URL-escaped by AuthService)
    public record SentEmail(SentEmailKind Kind, string To, Guid? UserId = null, string? Token = null);

    /// <summary>
    /// Replaces the SMTP EmailService in integration tests: nothing leaves the machine, and tests can
    /// read the confirmation and reset tokens from the recorded emails. Registered as a singleton
    /// shared by every test class; ResetDataBaseAsync calls <see cref="Clear"/>.
    /// </summary>
    public class FakeEmailService : IEmailService
    {
        private readonly ConcurrentQueue<SentEmail> _sent = new();
        private readonly ConcurrentDictionary<string, bool> _failingRecipients = new(StringComparer.OrdinalIgnoreCase);

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

        public Task SendEmailConfirmationAsync(string email, Guid userId, string token, CancellationToken ct = default)
        {
            ThrowIfFailing(email);
            _sent.Enqueue(new SentEmail(SentEmailKind.EmailConfirmation, email, userId, token));
            return Task.CompletedTask;
        }

        public Task SendPasswordResetAsync(string email, Guid userId, string token, CancellationToken ct = default)
        {
            ThrowIfFailing(email);
            _sent.Enqueue(new SentEmail(SentEmailKind.PasswordReset, email, userId, token));
            return Task.CompletedTask;
        }

        public Task SendAppointmentConfirmationAsync(string email, string patientName,
            string doctorName, DateOnly date, TimeOnly time, CancellationToken ct = default)
        {
            ThrowIfFailing(email);
            _sent.Enqueue(new SentEmail(SentEmailKind.AppointmentConfirmation, email));
            return Task.CompletedTask;
        }

        public Task SendAppointmentReminderAsync(string email, string patientName,
            string doctorName, DateOnly date, TimeOnly time, CancellationToken ct = default)
        {
            ThrowIfFailing(email);
            _sent.Enqueue(new SentEmail(SentEmailKind.AppointmentReminder, email));
            return Task.CompletedTask;
        }

        private void ThrowIfFailing(string recipient)
        {
            if (FailSends || _failingRecipients.ContainsKey(recipient))
                throw new InvalidOperationException($"SMTP unavailable for {recipient} (FakeEmailService)");
        }
    }
}
