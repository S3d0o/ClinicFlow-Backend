using Domain.Entities.Messaging;

namespace Domain.Interfaces.IRepositories
{
    public interface IMessageTemplateRepo
    {
        // The organization's templates, untracked, by key, channel and language; each filter is optional
        Task<List<MessageTemplate>> ListAsync(string? key, TemplateChannel? channel, Language? language, CancellationToken ct);
        // Tracked, for MessageTemplateService's changes
        Task<MessageTemplate?> GetByIdAsync(int templateId, CancellationToken ct);
        // The text to send, untracked; null when the organization has no row for it
        Task<MessageTemplate?> FindAsync(string key, TemplateChannel channel, Language language, CancellationToken ct);
    }
}
