using Domain.Entities.IdentityModule;
using Shared.DTOs.Auth;

namespace Services.Abstraction.Contracts
{
    public interface ITokenService
    {
        Task<TokenDto> GenerateTokensAsync(ApplicationUser user, IList<string> roles, string ipAddress, CancellationToken ct);

        //since we load the user we can directly return the LoginResponse instead of TokenDto
        //so that we can return the user details along with the tokens
        Task<Result<LoginResponse>> RefreshTokenAsync(string refreshToken, string ipAddress, CancellationToken ct);

        // Revokes a refresh token. With a userId (a logged-in caller) the token must belong to that user;
        // without one (an anonymous logout), holding the token is enough
        Task<Result> RevokeRefreshTokenAsync(Guid? userId, string refreshToken, string ipAddress, string? reason, CancellationToken ct);
    }
}
