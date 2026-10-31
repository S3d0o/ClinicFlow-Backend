using Domain.Entities.IdentityModule;
using Shared.Authorization;

namespace Services.Abstraction.Contracts
{
    // What a signed-in user's access token says about them (ADR 0002; API_CONTRACT.md §2.1)
    public sealed record UserAccess(
        IReadOnlyList<string> Roles,
        int OrganizationId,
        int? StaffMemberId,
        bool IsMedicalDirector,
        IReadOnlyList<StaffAssignment> Assignments)
    {
        // The single role of the inherited LoginResponse: the first of Roles.All the user holds (REVIEW.md NL10)
        public string PrimaryRole => Shared.Authorization.Roles.All.FirstOrDefault(Roles.Contains) ?? Roles.FirstOrDefault() ?? "";
    }

    // Works out a user's roles and assignments when a token is issued (login, refresh)
    public interface IUserAccessService
    {
        Task<UserAccess> GetAsync(ApplicationUser user, CancellationToken ct);
    }
}
