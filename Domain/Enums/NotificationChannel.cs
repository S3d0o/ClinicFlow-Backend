namespace Domain.Enums
{
    // How a user wants booking confirmations and reminders (FR-NTF-04). Phone is WhatsApp, or SMS when it is on.
    // Stored as a string
    public enum NotificationChannel
    {
        Phone = 1,
        Email = 2,
        Both = 3
    }
}
