using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Authorization;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace ClinicFlow.IntegrationTests.Infrastructure
{
    // Signs a request in from headers instead of a JWT. Without the scope headers a role has no assignment, so its
    // Branch grants reach every branch (P0 behaviour, EffectiveAccess); send X-Test-RoleScope to test branch scope
    public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string UserIdHeader = "X-Test-UserId";
        public const string UserRoleHeader = "X-Test-Role";              // one or more roles, comma-separated
        public const string RoleScopeHeader = "X-Test-RoleScope";        // role_scope claims, e.g. "Manager=2" or "Admin=*"
        public const string StaffMemberHeader = "X-Test-StaffMemberId";
        public const string MedicalDirectorHeader = "X-Test-MedicalDirector"; // "true"

        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder
            ) : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
           if(!Request.Headers.TryGetValue( UserIdHeader, out var userId))
                return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
            claims.AddRange(Values(UserRoleHeader).Select(role => new Claim(ClaimTypes.Role, role)));
            claims.AddRange(Values(RoleScopeHeader).Select(scope => new Claim(ClinicClaims.RoleScope, scope)));
            if (Request.Headers.TryGetValue(StaffMemberHeader, out var staffMemberId))
                claims.Add(new Claim(ClinicClaims.StaffMember, staffMemberId.ToString()));
            if (Request.Headers.TryGetValue(MedicalDirectorHeader, out var medicalDirector))
                claims.Add(new Claim(ClinicClaims.MedicalDirector, medicalDirector.ToString()));

            var identity = new ClaimsIdentity(claims, "Test");
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, "Test");

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        // A role scope holds commas itself ("Doctor=1,3"), so only roles are split; repeat the header for several scopes
        private IEnumerable<string> Values(string header)
        {
            if (!Request.Headers.TryGetValue(header, out var values))
                return [];
            return header == UserRoleHeader
                ? values.SelectMany(v => (v ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                : values.OfType<string>();
        }
    }
}
