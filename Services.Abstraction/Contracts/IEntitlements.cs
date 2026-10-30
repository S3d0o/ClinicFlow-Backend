namespace Services.Abstraction.Contracts
{
    // What the current organization's subscription allows (docs/v2/adr/0008 decision 7): its effective modules and
    // limits, computed once per organization and cached until its subscription or overrides change. The only reader
    // of the subscription tables. Module codes are Shared.Authorization.Modules, limit keys Shared.Authorization.Limits
    public interface IEntitlements
    {
        Task<bool> IsEnabledAsync(string module, CancellationToken ct);

        // The plan's value, or null when the plan sets no such limit (or the organization has no subscription)
        Task<int?> LimitAsync(string key, CancellationToken ct);

        // Core modules, the plan's and the overrides', closed over dependencies
        Task<IReadOnlySet<string>> EnabledModulesAsync(CancellationToken ct);
    }
}
