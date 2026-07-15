using Domain.Entities.IdentityModule;

namespace Domain.Interfaces.IRepositories
{
    public interface IRefreshTokenRepo
    {

        // Add a new refresh token
        public Task AddAsync(RefreshToken refreshToken);
        // Get a refresh token by its hash
        public Task<RefreshToken?> GetRefreshTokenByHashAsync(string tokenHash);

        public Task<IEnumerable<RefreshToken>> GetActiveByUserIdAsync(Guid userId);
    }
}
