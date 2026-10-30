namespace Domain.Enums;

// Where an organization's subscription stands (docs/v2/adr/0008 decision 13). Stored as a string. Lapse handling
// (read-only after the grace period) arrives with the platform phase; until then every status has full use
public enum SubscriptionStatus
{
    Trial = 1,
    Active = 2,
    PastDue = 3,
    Cancelled = 4
}
