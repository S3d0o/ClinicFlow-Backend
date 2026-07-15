namespace Shared.DTOs.Auth
{
    public record LoginResponse(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt,
    string Email,
    string FullName,
    string Role);
}
