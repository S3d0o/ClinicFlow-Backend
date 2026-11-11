using Domain.Entities.IdentityModule;

namespace Services.Abstraction.Contracts
{
    // Sends a user one of the catalog's messages (Shared MessageTemplateCatalog) from the organization's template, in
    // the user's preferred language (R10). Called by the outbox handlers, inside Delivery.OnceAsync
    public interface ITemplatedMessenger
    {
        // values: one for each placeholder the key offers, except clinicName, which is filled in here. Dates and times
        // are already written for the user's language (MessageFormats). Throws when the email can't be sent, so the
        // outbox retries
        Task SendEmailAsync(ApplicationUser recipient, string templateKey, IReadOnlyDictionary<string, string> values, CancellationToken ct);
    }
}
