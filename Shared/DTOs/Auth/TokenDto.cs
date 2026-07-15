namespace Shared.DTOs.Auth
{
    public record class TokenDto(
        string AccessToken,
        string RefreshToken,
        DateTime AccessTokenExpiresAt);
}
