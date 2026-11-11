using Domain.Enums;
using Shared.Authorization;
using System.Text.Json.Serialization;

namespace Shared.DTOs.Me
{
    // GET /api/v2/me (API_CONTRACT.md §13.2 Me). Roles and permissions are the access token's, so they match what the
    // API allows until the next refresh. PreferredLanguage ("ar" or "en") and NotificationChannel are the user's
    // (FR-ACC-06, FR-NTF-04); HasDoctorApplication and the MFA fields are fixed until R2
    public sealed record MeResponse(
        Guid Id,
        string FullName,
        string Phone,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Email,
        Language PreferredLanguage,
        NotificationChannel NotificationChannel,
        IReadOnlyList<RoleAssignmentResponse> Roles,
        IReadOnlyList<PermissionGrantResponse> Permissions,
        bool PhoneVerified,
        bool EmailConfirmed,
        bool HasDoctorApplication,
        bool MfaEnabled,
        bool MfaEnrolled,
        bool IsMedicalDirector);

    // Scope is Organization or Branch; BranchIds only for Branch
    public sealed record RoleAssignmentResponse(
        string Role,
        PermissionScope Scope,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<int>? BranchIds);

    // Scope is Own, Branch or Organization; BranchIds only for Branch, and missing for a Branch grant that reaches
    // every branch (a role without an assignment)
    public sealed record PermissionGrantResponse(
        string Permission,
        PermissionScope Scope,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<int>? BranchIds);
}
