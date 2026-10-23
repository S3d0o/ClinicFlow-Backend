namespace Services.Abstraction.Contracts
{
    public record AccessToken(string Value, DateTime ExpiresAtUtc);

    // Issues the signed access token (JWT) a logged-in user sends as "Authorization: Bearer"
    public interface IAccessTokenFactory
    {
        AccessToken Create(Guid userId, string email, string fullName, IEnumerable<string> roles);
    }
}
