namespace Shared.DTOs.Auth
{
    public record ConfirmEmailRequest(
     string UserId,
     string Token);
}
