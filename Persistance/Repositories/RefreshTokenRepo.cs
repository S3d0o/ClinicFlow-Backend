namespace Persistence.Repositories
{
    public class RefreshTokenRepo(ClinicDbContext context) : IRefreshTokenRepo
    {
        public async Task AddAsync(RefreshToken refreshToken)
         => await context.RefreshTokens.AddAsync(refreshToken);

        public async Task<IEnumerable<RefreshToken>> GetActiveByUserIdAsync(Guid userId)
         => await context.RefreshTokens
                 .Where(r => r.UserId == userId
                      && r.Revoked == null
                      && r.Expires > DateTime.UtcNow)
                     .ToListAsync();

        public async Task<RefreshToken?> GetRefreshTokenByHashAsync(string tokenHash)
        => await context.RefreshTokens.FirstOrDefaultAsync(s => s.TokenHash == tokenHash);
    }
}
