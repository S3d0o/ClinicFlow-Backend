namespace Persistence.Repositories
{
    public class RefreshTokenRepo(ClinicDbContext context) : IRefreshTokenRepo
    {
        public async Task AddAsync(RefreshToken refreshToken, CancellationToken ct)
            => await context.RefreshTokens.AddAsync(refreshToken, ct);

        public async Task<RefreshToken?> GetRefreshTokenByHashAsync(string tokenHash, CancellationToken ct)
            => await context.RefreshTokens.FirstOrDefaultAsync(s => s.TokenHash == tokenHash, ct);

        public async Task<int> RevokeAllActiveAsync(Guid userId, string reason, string? revokedByIp, CancellationToken ct)
        {
            var now = DateTime.UtcNow;

            return await context.RefreshTokens
                .Where(r => r.UserId == userId && r.Revoked == null && r.Expires > now)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Revoked, now)
                    .SetProperty(r => r.RevokedByIp, revokedByIp)
                    .SetProperty(r => r.RevocationReason, reason), ct);
        }
    }
}
