using Domain.Entities.IdentityModule;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Shared.DTOs.Auth;
using Shared.Settings;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using JwtRegisteredClaimNames = Microsoft.IdentityModel.JsonWebTokens.JwtRegisteredClaimNames;

namespace Services.Implementations
{
    public class TokenService(
        IOptions<JwtSettings> jwtOptions,
        UserManager<ApplicationUser> userManager,
        IUnitOfWork uow) : ITokenService
    {
        private readonly JwtSettings _jwt = jwtOptions.Value;
        public async Task<TokenDto> GenerateTokensAsync(ApplicationUser user, IList<string> roles, string ipAddress)
        {
            if(user == null) 
                throw new ArgumentNullException(nameof(user));
            roles ??= new List<string>();

            var accessToken = GenerateAccessToken(user, roles);

            var securityStamp = await userManager.GetSecurityStampAsync(user);

            var RefreshTokenExpirationDays = _jwt.RefreshTokenExpirationDays;

            var refreshToken = GenerateRefreshToken();
            var refreshTokenHash = HashRefreshToken(refreshToken);
            var refreshTokenEntity = CreateRefreshEntity(refreshTokenHash, user.Id, ipAddress, RefreshTokenExpirationDays, securityStamp);

            await uow.RefreshTokens.AddAsync(refreshTokenEntity);
            await uow.SaveChangesAsync();

            var ATexpiresAt = DateTime.UtcNow.AddMinutes(_jwt.AccessTokenExpirationMinutes);

            return new TokenDto(accessToken, refreshToken, ATexpiresAt);

        }


        public async Task<LoginResponse> RefreshTokenAsync(string refreshToken, string ipAddress)
        {
            var tokenHash = HashRefreshToken(refreshToken);

            using var transaction = await uow.BeginTransactionAsync();

            var existingToken = await uow.RefreshTokens.GetRefreshTokenByHashAsync(tokenHash);

            if (existingToken == null)
                throw new SecurityTokenException("Invalid refresh token");

            if (!existingToken.IsActive)
                throw new SecurityTokenException("Inactive refresh token");

            if (existingToken.Revoked != null)
                throw new SecurityTokenException("Refresh token already revoked");

            if (existingToken.IsExpired)
                throw new SecurityTokenException("Refresh token expired");

            if (!string.IsNullOrEmpty(existingToken.ReplacedByTokenHash))
                throw new SecurityTokenException("Refresh token already rotated");

            // Theft detection
            if (existingToken.LastUsed != null)
            {
                var minutes = (DateTime.UtcNow - existingToken.LastUsed.Value).TotalMinutes;
                if (minutes < _jwt.TheftDetectionWindowMinutes && existingToken.LastUsedByIp != ipAddress)
                    throw new SecurityTokenException("Suspicious refresh token activity detected");
            }

            // Load user for security-stamp check
            var user = await userManager.FindByIdAsync(existingToken.UserId.ToString())
                ?? throw new SecurityTokenException("Invalid token - User not found");

            var currentStamp = await userManager.GetSecurityStampAsync(user);
            if (existingToken.SecurityStamp != currentStamp)
                throw new SecurityTokenException("Refresh token invalidated due to user security changes");

            // Mark existing token as rotated
            existingToken.LastUsed = DateTime.UtcNow;
            existingToken.LastUsedByIp = ipAddress;
            existingToken.RevocationReason = "Rotated";
            existingToken.Revoked = DateTime.UtcNow;
            existingToken.RevokedByIp = ipAddress;

            // Generate new refresh token
            var newRefreshToken = GenerateRefreshToken();
            var newRefreshTokenHash = HashRefreshToken(newRefreshToken);
            existingToken.ReplacedByTokenHash = newRefreshTokenHash;
            var RefreshTokenExpirationDays = _jwt.RefreshTokenExpirationDays;

            var newTokenEntity = CreateRefreshEntity(
                newRefreshTokenHash,
                user.Id,
                ipAddress,
                RefreshTokenExpirationDays,
                currentStamp
            );

            await uow.RefreshTokens.AddAsync(newTokenEntity);

            try
            {
                await uow.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                await transaction.RollbackAsync();
                throw new SecurityTokenException("Refresh token already used");
            }
            var AccessTokenExpirationMinutes = _jwt.AccessTokenExpirationMinutes;

            // New access token
            var roles = await userManager.GetRolesAsync(user);
            return new LoginResponse(
                AccessToken: GenerateAccessToken(user, roles),
                RefreshToken: newRefreshToken,
                ExpiresAt: DateTime.UtcNow.AddMinutes(AccessTokenExpirationMinutes),
                Email: user.Email ?? "",
                FullName: string.Join(" ", user.FirstName, user.LastName),
                Role: roles.FirstOrDefault() ?? ""
            );
        }

        public async Task RevokeRefreshTokenAsync(string refreshToken, string ipAddress, string? reason = null)
        {
            var tokenHash = HashRefreshToken(refreshToken);
            var existingToken = await uow.RefreshTokens.GetRefreshTokenByHashAsync(tokenHash);

            if (existingToken == null)
                throw new KeyNotFoundException("Refresh token not found");

            if (existingToken.Revoked == null)
            {
                existingToken.Revoked = DateTime.UtcNow;
                existingToken.RevokedByIp = ipAddress;
                existingToken.RevocationReason = reason ?? "RevokedByUser";
                await uow.SaveChangesAsync();
            }
        }

        #region Helpers 
        private string GenerateAccessToken(ApplicationUser user, IList<string> roles)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email ?? ""),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Name, $"{user.FirstName} {user.LastName}")
            };
            foreach (var role in roles)
                claims.Add(new Claim(ClaimTypes.Role,role));

            var expiration = DateTime.UtcNow.AddMinutes(_jwt.AccessTokenExpirationMinutes);

            var key  = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Secret));

            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _jwt.Issuer,
                audience: _jwt.Audience,
                claims: claims,
                signingCredentials: creds,
                expires: expiration
                );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
        private string GenerateRefreshToken() =>
          Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

        private string HashRefreshToken(string raw) =>
            Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

        private RefreshToken CreateRefreshEntity(string tokenHash, Guid userId, string ip, int days, string? securityStamp) =>
            new RefreshToken
            {
                TokenHash = tokenHash,
                UserId = userId,
                Expires = DateTime.UtcNow.AddDays(days),
                Created = DateTime.UtcNow,
                CreatedByIp = ip,
                SecurityStamp = securityStamp
            };

        #endregion
    }
}
