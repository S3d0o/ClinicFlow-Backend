namespace Services.Abstraction.Contracts
{
    public record AccessToken(string Value, DateTime ExpiresAtUtc);

    // Issues the signed access token (JWT) a logged-in user sends as "Authorization: Bearer"
    public interface IAccessTokenFactory
    {
        // Roles as role claims; for staff also the organization, staff member, a role_scope per role and the medical
        // director flag (Shared.Authorization.ClinicClaims)
        AccessToken Create(Guid userId, string email, string fullName, UserAccess access);
    }
}
