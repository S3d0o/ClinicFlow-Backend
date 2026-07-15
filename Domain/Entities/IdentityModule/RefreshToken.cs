using System.ComponentModel.DataAnnotations;

namespace Domain.Entities.IdentityModule
{
    public class RefreshToken
    {
        public int Id { get; set; }
        public string TokenHash { get; set; } = null!;
        public Guid UserId { get; set; } = Guid.Empty;
        public DateTime Expires { get; set; }
        public DateTime Created { get; set; }
        public string CreatedByIp { get; set; } = null!;
        public DateTime? Revoked { get; set; }
        public string? RevokedByIp { get; set; }
        public string? ReplacedByTokenHash { get; set; }
        public bool IsActive => Revoked == null && !IsExpired;
        public bool IsExpired => DateTime.UtcNow >= Expires;
        public DateTime? LastUsed { get; set; }
        public string? LastUsedByIp { get; set; }
        public string? RevocationReason { get; set; }
        public string? SecurityStamp { get; set; }

        // Enables concurrency checks
        [Timestamp]
        public byte[] RowVersion { get; set; } = null!;
    }
}
