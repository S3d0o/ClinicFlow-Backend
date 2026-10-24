namespace Domain.Enums;

// What an audit entry records (05 §3, AuditEntry). Stored as a string, so the actions later phases add
// (Read, Approve, BreakGlass, Export) need no data migration
public enum AuditAction
{
    Create = 1,
    Update = 2,
    Delete = 3
}
