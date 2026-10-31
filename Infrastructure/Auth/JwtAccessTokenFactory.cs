using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Shared.Settings;
using System.IdentityModel.Tokens.Jwt;
using Shared.Authorization;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using JwtRegisteredClaimNames = Microsoft.IdentityModel.JsonWebTokens.JwtRegisteredClaimNames;

namespace Infrastructure.Auth;

// HMAC-SHA256 JWTs, validated by the host's JwtBearer setup (InfraStructureServiceExtensions). The payload's claim keys
// are part of the API contract (§2.1): the ClaimTypes URIs for id, name and role, plus email and jti, and the V2 claims
// of ClinicClaims (org, staff, role_scope, med_dir)
public class JwtAccessTokenFactory(IOptions<JwtSettings> jwtOptions, IClock clock) : IAccessTokenFactory
{
    private readonly JwtSettings _jwt = jwtOptions.Value;

    public AccessToken Create(Guid userId, string email, string fullName, UserAccess access)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(JwtRegisteredClaimNames.Email, email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.Name, fullName)
        };
        claims.AddRange(access.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
        claims.Add(new Claim(ClinicClaims.Organization, access.OrganizationId.ToString(CultureInfo.InvariantCulture)));
        if (access.StaffMemberId is { } staffMemberId)
            claims.Add(new Claim(ClinicClaims.StaffMember, staffMemberId.ToString(CultureInfo.InvariantCulture)));
        claims.AddRange(access.Assignments.Select(a => new Claim(ClinicClaims.RoleScope, ClinicClaims.RoleScopeValue(a))));
        if (access.IsMedicalDirector)
            claims.Add(new Claim(ClinicClaims.MedicalDirector, "true"));

        var expiresAt = clock.UtcNow.AddMinutes(_jwt.AccessTokenExpirationMinutes);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Secret));

        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Audience,
            claims: claims,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
            expires: expiresAt);

        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
