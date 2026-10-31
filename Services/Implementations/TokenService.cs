using Domain.Entities.IdentityModule;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Shared.DTOs.Auth;
using Shared.Settings;
using System.Security.Cryptography;
using System.Text;

namespace Services.Implementations
{
    public class TokenService(
        IOptions<JwtSettings> jwtOptions,
        UserManager<ApplicationUser> userManager,
        IUnitOfWork uow,
        IAccessTokenFactory accessTokenFactory,
        IUserAccessService userAccess,
        IClock clock,
        ILogger<TokenService> logger) : ITokenService
    {
        private const string RotatedReason = "Rotated";
        private const string ReuseDetectedReason = "Reuse of rotated refresh token detected";

        private readonly JwtSettings _jwt = jwtOptions.Value;

        public async Task<TokenDto> GenerateTokensAsync(ApplicationUser user, UserAccess access, string ipAddress, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(user);

            var accessToken = CreateAccessToken(user, access);

            var securityStamp = await userManager.GetSecurityStampAsync(user);

            var refreshToken = GenerateRefreshToken();
            var refreshTokenHash = HashRefreshToken(refreshToken);
            var refreshTokenEntity = CreateRefreshEntity(refreshTokenHash, user.Id, ipAddress, _jwt.RefreshTokenExpirationDays, securityStamp);

            await uow.RefreshTokens.AddAsync(refreshTokenEntity, ct);
            await uow.SaveChangesAsync(ct);

            return new TokenDto(accessToken.Value, refreshToken, accessToken.ExpiresAtUtc);
        }

        // Expected failures return AuthErrors.InvalidRefreshToken (401). Anything else (DB outage,
        // cancellation) propagates, so infrastructure problems are not reported as "please log in again".
        public async Task<Result<LoginResponse>> RefreshTokenAsync(string refreshToken, string ipAddress, CancellationToken ct)
        {
            var tokenHash = HashRefreshToken(refreshToken);

            var existingToken = await uow.RefreshTokens.GetRefreshTokenByHashAsync(tokenHash, ct);
            if (existingToken is null)
            {
                logger.LogWarning("Refresh failed: unknown refresh token from {IpAddress}", ipAddress);
                return AuthErrors.InvalidRefreshToken;
            }

            // A rotated token is being presented again: either a client race or a stolen token.
            if (existingToken.ReplacedByTokenHash is not null)
                return await HandleRotatedTokenReuseAsync(existingToken, ipAddress, ct);

            if (!existingToken.IsActiveAt(clock.UtcNow))
            {
                logger.LogWarning("Refresh failed: token {TokenId} is revoked or expired", existingToken.Id);
                return AuthErrors.InvalidRefreshToken;
            }

            var user = await userManager.FindByIdAsync(existingToken.UserId.ToString());
            if (user is null || !user.IsActive)
            {
                logger.LogWarning("Refresh failed: user {UserId} not found or suspended", existingToken.UserId);
                return AuthErrors.InvalidRefreshToken;
            }

            var currentStamp = await userManager.GetSecurityStampAsync(user);
            if (existingToken.SecurityStamp != currentStamp)
            {
                logger.LogWarning("Refresh failed: security stamp changed for user {UserId}", user.Id);
                return AuthErrors.InvalidRefreshToken;
            }

            // Mark existing token as rotated
            var now = clock.UtcNow;
            var newRefreshToken = GenerateRefreshToken();
            var newRefreshTokenHash = HashRefreshToken(newRefreshToken);

            existingToken.LastUsed = now;
            existingToken.LastUsedByIp = ipAddress;
            existingToken.Revoked = now;
            existingToken.RevokedByIp = ipAddress;
            existingToken.RevocationReason = RotatedReason;
            existingToken.ReplacedByTokenHash = newRefreshTokenHash;

            await uow.RefreshTokens.AddAsync(
                CreateRefreshEntity(newRefreshTokenHash, user.Id, ipAddress, _jwt.RefreshTokenExpirationDays, currentStamp), ct);

            // RowVersion on RefreshToken: a concurrent refresh with the same token fails here
            if (!await uow.TrySaveChangesAsync(ct))
            {
                logger.LogWarning("Refresh failed: token {TokenId} was used concurrently", existingToken.Id);
                return AuthErrors.InvalidRefreshToken;
            }

            // Role changes take effect here, at the next refresh (FR-STF-03)
            var access = await userAccess.GetAsync(user, ct);
            var accessToken = CreateAccessToken(user, access);
            return new LoginResponse(
                AccessToken: accessToken.Value,
                RefreshToken: newRefreshToken,
                ExpiresAt: accessToken.ExpiresAtUtc,
                Email: user.Email ?? "",
                FullName: string.Join(" ", user.FirstName, user.LastName),
                Role: access.PrimaryRole
            );
        }

        public async Task<Result> RevokeRefreshTokenAsync(Guid? userId, string refreshToken, string ipAddress, string? reason, CancellationToken ct)
        {
            var tokenHash = HashRefreshToken(refreshToken);
            var existingToken = await uow.RefreshTokens.GetRefreshTokenByHashAsync(tokenHash, ct);

            // A logged-in user may only revoke their own tokens. An anonymous caller (whose access token
            // may have expired) proves ownership by holding the token, which could refresh anyway (REVIEW.md L7)
            if (existingToken is null || (userId is not null && existingToken.UserId != userId))
            {
                logger.LogWarning("Revoke failed: refresh token not found for user {UserId}", userId);
                return AuthErrors.InvalidRefreshToken;
            }

            if (existingToken.Revoked == null)
            {
                existingToken.Revoked = clock.UtcNow;
                existingToken.RevokedByIp = ipAddress;
                existingToken.RevocationReason = reason ?? "RevokedByUser";
                await uow.SaveChangesAsync(ct);
            }

            return Result.Ok();
        }

        #region Helpers

        // Reuse within the theft-detection window from the same IP is treated as a client race
        // (e.g. two tabs refreshing at once) and only rejected. Any other reuse means the token
        // may have been stolen, so every active session of the user is revoked.
        private async Task<Result<LoginResponse>> HandleRotatedTokenReuseAsync(RefreshToken token, string ipAddress, CancellationToken ct)
        {
            var isLikelyRace = token.LastUsed is not null
                && token.LastUsedByIp == ipAddress
                && (clock.UtcNow - token.LastUsed.Value).TotalMinutes < _jwt.TheftDetectionWindowMinutes;

            if (isLikelyRace)
            {
                logger.LogWarning("Refresh failed: rotated token {TokenId} reused from the same IP within the detection window", token.Id);
                return AuthErrors.InvalidRefreshToken;
            }

            var revoked = await uow.RefreshTokens.RevokeAllActiveAsync(token.UserId, ReuseDetectedReason, ipAddress, clock.UtcNow, ct);
            logger.LogWarning(
                "Rotated refresh token {TokenId} reused from {IpAddress}; revoked {Count} active tokens of user {UserId}",
                token.Id, ipAddress, revoked, token.UserId);

            return AuthErrors.InvalidRefreshToken;
        }

        private AccessToken CreateAccessToken(ApplicationUser user, UserAccess access)
            => accessTokenFactory.Create(user.Id, user.Email ?? "", $"{user.FirstName} {user.LastName}", access);

        private string GenerateRefreshToken() =>
          Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

        private string HashRefreshToken(string raw) =>
            Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

        private RefreshToken CreateRefreshEntity(string tokenHash, Guid userId, string ip, int days, string? securityStamp)
        {
            var now = clock.UtcNow;
            return new RefreshToken
            {
                TokenHash = tokenHash,
                UserId = userId,
                Expires = now.AddDays(days),
                Created = now,
                CreatedByIp = ip,
                SecurityStamp = securityStamp
            };
        }

        #endregion
    }
}
