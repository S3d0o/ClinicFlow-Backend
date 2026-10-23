using Domain.Entities.IdentityModule;

namespace Domain.Interfaces.IRepositories
{
    public interface IRefreshTokenRepo
    {
        // Add a new refresh token
        Task AddAsync(RefreshToken refreshToken, CancellationToken ct);

        // Get a refresh token by its hash
        Task<RefreshToken?> GetRefreshTokenByHashAsync(string tokenHash, CancellationToken ct);

        // Revokes every refresh token of the user still active at utcNow, directly in the database (no SaveChangesAsync needed)
        Task<int> RevokeAllActiveAsync(Guid userId, string reason, string? revokedByIp, DateTime utcNow, CancellationToken ct);
    }
}
