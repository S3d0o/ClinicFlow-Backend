namespace Shared.DTOs.Auth
{
    public record ResetPasswordRequest(
    string UserId,
    string Token,
    string NewPassword);
}
