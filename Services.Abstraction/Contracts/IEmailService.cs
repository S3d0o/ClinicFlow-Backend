namespace Services.Abstraction.Contracts
{
    // Sends an email that is already written: the text comes from a message template in the recipient's language
    // (ITemplatedMessenger, FR-NTF-02), and the adapter only delivers it
    public interface IEmailService
    {
        Task SendAsync(EmailMessage message, CancellationToken ct);
    }

    // TextBody and HtmlBody are the same message, for mail clients that show one or the other. TemplateKey says which
    // message it is, for the logs
    public sealed record EmailMessage(
        string ToAddress, string ToName, string Subject, string TextBody, string HtmlBody, string TemplateKey);
}
