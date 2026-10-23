namespace Domain.Enums
{
    // Stored as strings, so new values need no data migration (docs/v2/adr/0004, 05 §4.1)
    public enum AppointmentStatus
    {
        Pending,
        Confirmed,
        Cancelled,
        Completed,
        NoShow,
        CheckedIn,
        InProgress,
        Rescheduled
    }
}
