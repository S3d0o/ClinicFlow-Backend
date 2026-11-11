namespace Domain.Entities.Messaging
{
    // The text of one message on one channel in one language (FR-NTF-02), e.g. AppointmentConfirmed by email in
    // Arabic. Placeholders are written {{name}}; the ones a key offers and its default texts are in Shared's
    // MessageTemplateCatalog, from which the rows are created at startup. Audited
    public class MessageTemplate : BaseEntity<int>, IOrganizationOwned
    {
        public int OrganizationId { get; set; }
        public string Key { get; set; } = string.Empty;
        public TemplateChannel Channel { get; set; }
        public Language Language { get; set; }
        public string? Subject { get; set; } // Email only
        public string Body { get; set; } = string.Empty;

        // WhatsApp only: the name Meta approved the text under; the message is sent by that name and the language
        public string? ProviderTemplateName { get; set; }

        // True while the text is the catalog's: such a row follows the catalog when its default text changes
        public bool IsDefault { get; set; }
        public DateTime UpdatedAt { get; set; }

        public void SetText(string? subject, string body, bool isDefault, DateTime utcNow)
        {
            Subject = subject;
            Body = body;
            IsDefault = isDefault;
            UpdatedAt = utcNow;
        }
    }
}
