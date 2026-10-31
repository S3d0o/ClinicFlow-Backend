using Shared.DTOs.Me;

namespace Services.Abstraction.Contracts
{
    // The signed-in user as the V2 frontend sees them (API_CONTRACT.md §13.4)
    public interface IMeService
    {
        // Profile.NotFound when the account no longer exists
        Task<Result<MeResponse>> GetAsync(CancellationToken ct);
    }
}
