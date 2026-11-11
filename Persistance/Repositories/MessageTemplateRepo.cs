using Domain.Enums;

namespace Persistence.Repositories
{
    public class MessageTemplateRepo(ClinicDbContext context) : IMessageTemplateRepo
    {
        public Task<List<MessageTemplate>> ListAsync(string? key, TemplateChannel? channel, Language? language, CancellationToken ct)
            => context.MessageTemplates.AsNoTracking()
                .Where(t => (key == null || t.Key == key) && (channel == null || t.Channel == channel) && (language == null || t.Language == language))
                .OrderBy(t => t.Key).ThenBy(t => t.Channel).ThenBy(t => t.Language)
                .ToListAsync(ct);

        public Task<MessageTemplate?> GetByIdAsync(int templateId, CancellationToken ct)
            => context.MessageTemplates.FirstOrDefaultAsync(t => t.Id == templateId, ct);

        public Task<MessageTemplate?> FindAsync(string key, TemplateChannel channel, Language language, CancellationToken ct)
            => context.MessageTemplates.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Key == key && t.Channel == channel && t.Language == language, ct);
    }
}
