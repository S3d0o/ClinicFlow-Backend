namespace Domain.Enums
{
    // The channel a message template is written for (FR-NTF-02). Stored as a string.
    // WhatsApp texts are approved by Meta, so they can't be edited here
    public enum TemplateChannel
    {
        Email = 1,
        WhatsApp = 2,
        Sms = 3
    }
}
