namespace Domain.Enums
{
    // Stored as a string. "Leaving" in the contract (§13.23) isn't stored: it is an Active member with an EndDate ahead
    public enum StaffStatus
    {
        Invited,
        Active,
        Deactivated
    }
}
