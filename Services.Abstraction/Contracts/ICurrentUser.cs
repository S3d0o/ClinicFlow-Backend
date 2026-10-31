using Shared.Authorization;

namespace Services.Abstraction.Contracts
{
    // Who is making the current request (ADR 0002). Implemented over the HTTP context in Presentation; services
    // and, from P0 PR 10, the audit trail read it instead of having the user passed down from controllers
    public interface ICurrentUser
    {
        bool IsAuthenticated { get; }

        // Throws when the request is anonymous: only read it where authentication is required
        Guid UserId { get; }

        // Role names from the access token (Shared.Authorization.Roles); empty when anonymous
        IReadOnlyList<string> Roles { get; }

        // From the V2 claims (Shared.Authorization.ClinicClaims): the staff member, where each role applies and the
        // medical director flag. Null, empty and false for patients and for tokens without them (P0 behaviour)
        int? StaffMemberId { get; }
        IReadOnlyList<StaffAssignment> Assignments { get; }
        bool IsMedicalDirector { get; }
        // The client's IP after the trusted-proxy rules of UseForwardedHeaders; null outside a request
        string? IpAddress { get; }

        bool IsInRole(string role);
    }
}
