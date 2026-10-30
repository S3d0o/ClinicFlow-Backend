using Shared.DTOs.Entitlements;

namespace Presentation.Validators
{
    public class SetModuleRequestValidator : AbstractValidator<SetModuleRequest>
    {
        public SetModuleRequestValidator()
        {
            // Core modules are always on (docs/v2/adr/0008 decision 4), so only an add-on can be switched
            RuleFor(x => x.Module)
                .NotEmpty().WithMessage("Module is required.")
                .Must(module => Modules.AddOns.Contains(module))
                .WithMessage($"Module must be an add-on: {string.Join(", ", Modules.Catalog.Where(Modules.AddOns.Contains))}.");
        }
    }
}
