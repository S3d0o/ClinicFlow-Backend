using Domain.Entities.IdentityModule;
using Shared.DTOs.Auth;

namespace Services.Abstraction.Contracts
{
    public interface ITokenService
    {
        Task<TokenDto> GenerateTokensAsync(ApplicationUser user, IList<string> roles, string ipAddress);

        //since we load the user we can directly return the LoginResponse instead of TokenDto
        //so that we can return the user details along with the tokens
        Task<LoginResponse> RefreshTokenAsync(string refreshToken, string ipAddress);
        Task RevokeRefreshTokenAsync(string refreshToken, string ipAddress, string? reason = null);
    }
}
