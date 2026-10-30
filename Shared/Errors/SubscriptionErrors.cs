using Domain.ResultPattern;

namespace Shared.Errors
{
    // What a clinic's plan doesn't include (docs/v2/adr/0008 decision 10), and the platform switches' errors. The 403s
    // are distinct from the permission 403s, so the frontend can offer an upgrade instead of saying "forbidden"
    public static class SubscriptionErrors
    {
        public const string ModuleNotEnabledCode = "Subscription.ModuleNotEnabled";
        public const string LimitReachedCode = "Subscription.LimitReached";

        public static Error ModuleNotEnabled(string module) =>
            Error.Forbidden(ModuleNotEnabledCode, $"The {module} module is not enabled for this organization");

        // Counted limits and quotas (decision 5) use it from the writes they guard, from P1 on
        public static Error LimitReached(string limit) =>
            Error.Forbidden(LimitReachedCode, $"This organization has reached its '{limit}' limit");

        // The organization has no subscription row (a new organization before onboarding, R4)
        public static readonly Error NotFound =
            Error.NotFound("Subscription.NotFound", "This organization has no subscription");

        public static Error PlanNotFound(string planCode) =>
            Error.NotFound("Subscription.PlanNotFound", $"There is no plan '{planCode}'");

        // Switching an add-on off while an enabled add-on needs it (decision 4)
        public static Error ModuleRequired(string module, IEnumerable<string> requiredBy) =>
            Error.Conflict("Subscription.ModuleRequired",
                $"The {module} module can't be switched off while {string.Join(", ", requiredBy)} needs it");

        // Core modules are always on (decision 4); only add-ons have overrides
        public static Error NotAnAddOn(string module) =>
            Error.Validation("Subscription.NotAnAddOn", $"'{module}' is not an add-on module");

        // The platform switches are off on this deployment (Platform:AllowPlanSwitching)
        public static readonly Error SwitchingDisabled =
            Error.Forbidden("Subscription.SwitchingDisabled", "Plans and add-ons can't be switched on this deployment");
    }
}
