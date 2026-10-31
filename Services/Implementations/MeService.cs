using Domain.Entities.IdentityModule;
using Microsoft.AspNetCore.Identity;
using Shared.DTOs.Me;

namespace Services.Implementations
{
    // GET /api/v2/me. Roles and permissions come from the access token through IAccessGuard, and a permission is
    // listed only while its module is enabled (ADR 0008), so the menus built from them match what the API allows
    public class MeService(
        UserManager<ApplicationUser> userManager,
        ICurrentUser currentUser,
        IAccessGuard accessGuard,
        IEntitlements entitlements,
        ILogger<MeService> logger) : IMeService
    {
        // Until P1 PR 5 stores the user's choices (FR-ACC-06): Arabic, and messages by email as today
        private const string DefaultLanguage = "ar";
        private const string DefaultChannel = "Email";

        public async Task<Result<MeResponse>> GetAsync(CancellationToken ct)
        {
            var user = await userManager.FindByIdAsync(currentUser.UserId.ToString());
            if (user is null)
            {
                logger.LogWarning("Me requested for user {UserId}, who no longer exists", currentUser.UserId);
                return ProfileErrors.NotFound;
            }

            var permissions = new List<PermissionGrantResponse>();
            foreach (var permission in Permissions.All)
            {
                if (accessGuard.ScopeOf(permission) is not { } scope || !await entitlements.IsEnabledAsync(Modules.Of(permission), ct))
                    continue;
                var branches = scope == PermissionScope.Branch ? accessGuard.AllowedBranchIds(permission) : null;
                permissions.Add(new PermissionGrantResponse(permission, scope, branches?.Order().ToList()));
            }

            return new MeResponse(
                user.Id,
                $"{user.FirstName} {user.LastName}",
                user.PhoneNumber ?? "",
                user.Email,
                DefaultLanguage,
                DefaultChannel,
                RoleAssignments(),
                permissions,
                PhoneVerified: user.PhoneNumberConfirmed,
                EmailConfirmed: user.EmailConfirmed,
                HasDoctorApplication: false, // doctor applications come with R2
                MfaEnabled: false,           // MFA comes with R2
                MfaEnrolled: user.TwoFactorEnabled,
                IsMedicalDirector: currentUser.IsMedicalDirector);
        }

        // A role assigned to branches lists them; any other (organization-wide, a patient, a token without
        // assignments) is Organization
        private List<RoleAssignmentResponse> RoleAssignments()
            => currentUser.Roles.Distinct()
                .Select(role => currentUser.Assignments.FirstOrDefault(a => a.Role == role) is { BranchIds: { } branches }
                    ? new RoleAssignmentResponse(role, PermissionScope.Branch, branches.Order().ToList())
                    : new RoleAssignmentResponse(role, PermissionScope.Organization, null))
                .ToList();
    }
}
