using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Infrastructure.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Services.Abstraction.Contracts;
using Shared.Authorization;
using Shared.Settings;

namespace ClinicFlow.Tests.Infrastructure.JwtAccessTokenFactoryTests
{
    // The token's claim keys are part of the API contract (§2.1) and are what the host's JwtBearer validation reads
    public class CreateTests
    {
        private static readonly DateTime Now = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);
        private static readonly Guid UserId = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

        private readonly JwtSettings _jwt = new()
        {
            Secret = "unit-tests-only-signing-key-0123456789abcdef",
            Issuer = "ClinicFlow",
            Audience = "ClinicFlowClient",
            AccessTokenExpirationMinutes = 15,
        };
        private readonly JwtAccessTokenFactory _sut;

        private static readonly UserAccess Patient = new(["Patient"], OrganizationId: 1, StaffMemberId: null, IsMedicalDirector: false, Assignments: []);

        public CreateTests()
        {
            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(Now);
            _sut = new JwtAccessTokenFactory(Options.Create(_jwt), clock);
        }

        [Fact]
        public void Create_WhenCalled_WritesTheClaimKeysListedInTheContract()
        {
            // docs/API_CONTRACT.md §2.1, "exact key in the payload"
            var token = _sut.Create(UserId, "mona@test.com", "Mona Hassan", Patient);

            var payload = new JwtSecurityTokenHandler().ReadJwtToken(token.Value).Payload;
            Assert.Equal(UserId.ToString(), payload["http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier"]);
            Assert.Equal("Mona Hassan", payload["http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name"]);
            Assert.Equal("Patient", payload["http://schemas.microsoft.com/ws/2008/06/identity/claims/role"]);
            Assert.Equal("mona@test.com", payload["email"]);
            Assert.True(Guid.TryParse(payload.Jti, out _));
            Assert.Equal("ClinicFlow", payload.Iss);
            Assert.Equal(["ClinicFlowClient"], payload.Aud);
        }

        [Fact]
        public void Create_WhenCalled_ExpiresAfterTheConfiguredLifetime()
        {
            var token = _sut.Create(UserId, "mona@test.com", "Mona Hassan", Patient);

            Assert.Equal(Now.AddMinutes(15), token.ExpiresAtUtc);
            Assert.Equal(Now.AddMinutes(15), new JwtSecurityTokenHandler().ReadJwtToken(token.Value).ValidTo);
        }

        [Fact]
        public void Create_WhenValidatedLikeTheHost_ResolvesTheUserIdAndEveryRole()
        {
            var token = _sut.Create(UserId, "mona@test.com", "Mona Hassan", new UserAccess(["Doctor", "Admin"], 1, null, false, []));

            // The host's TokenValidationParameters (InfraStructureServiceExtensions), minus the lifetime check
            var principal = new JwtSecurityTokenHandler().ValidateToken(token.Value, new TokenValidationParameters
            {
                ValidIssuer = _jwt.Issuer,
                ValidAudience = _jwt.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Secret)),
                ValidateLifetime = false,
            }, out _);

            Assert.Equal(UserId.ToString(), principal.FindFirstValue(ClaimTypes.NameIdentifier));
            Assert.Equal(["Admin", "Doctor"], principal.FindAll(ClaimTypes.Role).Select(c => c.Value).Order());
        }


        [Fact]
        public void Create_ForAStaffMember_WritesTheOrganizationStaffMemberRoleScopesAndMedicalDirector()
        {
            var access = new UserAccess(["Manager", "Doctor"], OrganizationId: 1, StaffMemberId: 12, IsMedicalDirector: true,
                Assignments: [new StaffAssignment("Manager", null), new StaffAssignment("Doctor", new HashSet<int> { 3, 1 })]);

            var token = _sut.Create(UserId, "hala@test.com", "Hala Adel", access);

            var claims = new JwtSecurityTokenHandler().ReadJwtToken(token.Value).Claims.ToList();
            Assert.Equal("1", claims.Single(c => c.Type == ClinicClaims.Organization).Value);
            Assert.Equal("12", claims.Single(c => c.Type == ClinicClaims.StaffMember).Value);
            Assert.Equal(["Doctor=1,3", "Manager=*"], claims.Where(c => c.Type == ClinicClaims.RoleScope).Select(c => c.Value).Order());
            Assert.Equal("true", claims.Single(c => c.Type == ClinicClaims.MedicalDirector).Value);
        }

        [Fact]
        public void Create_ForAPatient_HasNoStaffClaims()
        {
            var claims = new JwtSecurityTokenHandler().ReadJwtToken(_sut.Create(UserId, "mona@test.com", "Mona Hassan", Patient).Value).Claims.ToList();

            Assert.Equal("1", claims.Single(c => c.Type == ClinicClaims.Organization).Value);
            Assert.DoesNotContain(claims, c => c.Type is ClinicClaims.StaffMember or ClinicClaims.RoleScope or ClinicClaims.MedicalDirector);
        }
        [Fact]
        public void Create_WhenSignedWithAnotherKey_FailsValidation()
        {
            var token = _sut.Create(UserId, "mona@test.com", "Mona Hassan", Patient);

            Assert.ThrowsAny<SecurityTokenException>(() => new JwtSecurityTokenHandler().ValidateToken(token.Value,
                new TokenValidationParameters
                {
                    ValidIssuer = _jwt.Issuer,
                    ValidAudience = _jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("another-key-another-key-another-key-0123")),
                    ValidateLifetime = false,
                }, out _));
        }
    }
}
