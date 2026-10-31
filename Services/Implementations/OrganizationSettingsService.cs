using Domain.Entities.Tenancy;
using Shared.DTOs.Organization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Services.Implementations
{
    // The organization's settings (FR-ORG-05). A key is stored only once it differs from its default, as its JSON
    // under the contract's name, so a setting added later is one more property of OrganizationSettingsResponse and
    // nothing else. Reads go through OrganizationSettingsCache. Rows are audited, so new ones are inserted in a
    // transaction (their audit entry needs the id the insert gives them)
    public class OrganizationSettingsService(
        IUnitOfWork uow,
        ITenantContext tenant,
        OrganizationSettingsCache cache,
        ILogger<OrganizationSettingsService> logger) : IOrganizationSettings, IOrganizationSettingsService
    {
        // The contract's names (camelCase); a PATCH serialized with it holds only the keys that were sent
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public async Task<OrganizationSettingsResponse> GetAsync(CancellationToken ct)
        {
            var organizationId = tenant.OrganizationId;
            if (cache.TryGet(organizationId, out var cached))
                return cached;

            var version = cache.VersionOf(organizationId);
            var settings = Effective(await uow.OrganizationSettings.GetAllAsync(ct)).Deserialize<OrganizationSettingsResponse>(Json)!;
            cache.TrySet(organizationId, version, settings);
            return settings;
        }

        public async Task<Result<OrganizationSettingsResponse>> UpdateAsync(OrganizationSettingsPatchRequest request, CancellationToken ct)
        {
            var sent = JsonSerializer.SerializeToNode(Normalized(request), Json)!.AsObject()
                .ToDictionary(p => p.Key, p => p.Value!.ToJsonString());

            var (settings, changed) = await uow.ExecuteInTransactionAsync(async transaction =>
            {
                var stored = await uow.OrganizationSettings.GetForUpdateAsync(ct);
                var current = Effective(stored);
                var changed = new List<string>();

                foreach (var (key, value) in sent)
                {
                    if (current[key]!.ToJsonString() == value)
                        continue;

                    changed.Add(key);
                    if (stored.FirstOrDefault(s => s.Key == key) is { } row)
                        row.Value = value;
                    else
                    {
                        var added = new OrganizationSetting { Key = key, Value = value };
                        uow.OrganizationSettings.Add(added);
                        stored.Add(added);
                    }
                }

                await uow.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return (Effective(stored).Deserialize<OrganizationSettingsResponse>(Json)!, changed);
            }, ct);

            // After the commit, so no reader can cache the old values from rows another connection still sees
            cache.Invalidate(tenant.OrganizationId);

            logger.LogInformation("Organization settings changed: {Keys}", changed);
            return settings;
        }

        // Every key: the default, overridden by the stored value. A stored key the response no longer has is ignored
        private static JsonObject Effective(IEnumerable<OrganizationSetting> stored)
        {
            var settings = JsonSerializer.SerializeToNode(OrganizationSettingsResponse.Defaults, Json)!.AsObject();
            foreach (var setting in stored)
                if (settings.ContainsKey(setting.Key))
                    settings[setting.Key] = JsonNode.Parse(setting.Value);
            return settings;
        }

        // 100.0 and 100 are the same amount; without this they'd be stored, compared and audited as different values
        private static OrganizationSettingsPatchRequest Normalized(OrganizationSettingsPatchRequest r) => r with
        {
            NoShowFee = Trim(r.NoShowFee),
            DepositAmount = Trim(r.DepositAmount),
            ReceptionistDiscountCapPercent = Trim(r.ReceptionistDiscountCapPercent),
            AccountantDiscountCapPercent = Trim(r.AccountantDiscountCapPercent),
            RefundApprovalThreshold = Trim(r.RefundApprovalThreshold),
            CashVarianceThreshold = Trim(r.CashVarianceThreshold),
        };

        // Dividing by 1 with 28 decimal places drops the trailing zeros of a decimal's scale
        private static decimal? Trim(decimal? value) => value / 1.0000000000000000000000000000m;
    }
}
