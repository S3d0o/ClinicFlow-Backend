using Domain.Entities.Subscriptions;
using Microsoft.Extensions.Options;
using Shared.DTOs.Entitlements;
using Shared.Settings;

namespace Services.Implementations
{
    // Plan and add-on switches (docs/v2/adr/0008 decisions 4 and 6). Saving refreshes the organization's cached
    // entitlements (EntitlementSaveChangesInterceptor), and the subscription and overrides are audited
    public class SubscriptionService(IUnitOfWork uow, IOptions<PlatformSettings> settings, ILogger<SubscriptionService> logger)
        : ISubscriptionService
    {
        public async Task<Result> ChangePlanAsync(ChangePlanRequest request, CancellationToken ct)
        {
            if (!settings.Value.AllowPlanSwitching)
            {
                logger.LogWarning("Refused to switch the plan to {PlanCode}: Platform:AllowPlanSwitching is off", request.PlanCode);
                return SubscriptionErrors.SwitchingDisabled;
            }

            var plan = await uow.Subscriptions.GetPlanByCodeAsync(request.PlanCode, ct);
            if (plan is null)
            {
                logger.LogWarning("Refused to switch to plan {PlanCode}: there is no such plan", request.PlanCode);
                return SubscriptionErrors.PlanNotFound(request.PlanCode);
            }

            var subscription = await uow.Subscriptions.GetCurrentAsync(ct);
            if (subscription is null)
            {
                logger.LogWarning("Refused to switch to plan {PlanCode}: the organization has no subscription", request.PlanCode);
                return SubscriptionErrors.NotFound;
            }

            subscription.PlanId = plan.Id;
            subscription.Plan = plan;

            // An extra the new plan includes, or an add-on switched off that it doesn't include, is no longer an override
            var included = plan.Modules.Select(m => m.ModuleCode).ToHashSet();
            foreach (var moduleOverride in await uow.Subscriptions.GetOverridesAsync(ct))
                if (moduleOverride.Enabled == included.Contains(moduleOverride.ModuleCode))
                    uow.Subscriptions.RemoveOverride(moduleOverride);

            await uow.SaveChangesAsync(ct);
            logger.LogInformation("Organization switched to plan {PlanCode}", plan.Code);
            return Result.Ok();
        }

        public async Task<Result> SetModuleAsync(SetModuleRequest request, CancellationToken ct)
        {
            if (!settings.Value.AllowPlanSwitching)
            {
                logger.LogWarning("Refused to switch module {Module}: Platform:AllowPlanSwitching is off", request.Module);
                return SubscriptionErrors.SwitchingDisabled;
            }

            // Core modules are always on (decision 4); the validator refuses them too
            if (!Modules.AddOns.Contains(request.Module))
            {
                logger.LogWarning("Refused to switch module {Module}: it is not an add-on", request.Module);
                return SubscriptionErrors.NotAnAddOn(request.Module);
            }

            var subscription = await uow.Subscriptions.GetCurrentAsync(ct);
            if (subscription is null)
            {
                logger.LogWarning("Refused to switch module {Module}: the organization has no subscription", request.Module);
                return SubscriptionErrors.NotFound;
            }

            var planModules = subscription.Plan.Modules.Select(m => m.ModuleCode).ToList();
            var overrides = await uow.Subscriptions.GetOverridesAsync(ct);

            if (!request.Enabled)
            {
                var enabled = Modules.Effective(planModules, overrides.Select(o => (o.ModuleCode, o.Enabled)));
                var requiredBy = Modules.RequiredBy(request.Module, enabled);
                if (requiredBy.Count > 0)
                {
                    logger.LogWarning("Refused to switch module {Module} off: {RequiredBy} needs it", request.Module, requiredBy);
                    return SubscriptionErrors.ModuleRequired(request.Module, requiredBy);
                }
            }

            var existing = overrides.FirstOrDefault(o => o.ModuleCode == request.Module);
            if (request.Enabled == planModules.Contains(request.Module))
            {
                // Back to what the plan says needs no override
                if (existing is not null)
                    uow.Subscriptions.RemoveOverride(existing);
            }
            else if (existing is not null)
                existing.Enabled = request.Enabled;
            else
                uow.Subscriptions.AddOverride(new OrganizationModuleOverride { ModuleCode = request.Module, Enabled = request.Enabled });

            await uow.SaveChangesAsync(ct);
            logger.LogInformation("Module {Module} switched {State} for the organization", request.Module, request.Enabled ? "on" : "off");
            return Result.Ok();
        }
    }
}
