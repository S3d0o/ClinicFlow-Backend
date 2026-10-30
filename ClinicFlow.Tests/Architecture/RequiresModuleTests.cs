using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Presentation.Authorization;
using Presentation.Controllers;
using Shared.Authorization;

namespace ClinicFlow.Tests.Architecture
{
    // The second line of defence on add-on modules (docs/v2/adr/0008 decision 9): every action of a controller in an
    // add-on module's folder (ADR 0006: Presentation/Controllers/V2/Billing/…) carries [RequiresModule] for that
    // module, on the action or its controller. A folder is named after the module's constant in Shared.Authorization
    // .Modules (Billing → BIL, FrontDesk → FD, ...), so the namespace says which module a controller belongs to
    public class RequiresModuleTests
    {
        // Folder (= constant) name → module code, for every add-on
        private static readonly Dictionary<string, string> AddOnFolders = typeof(Modules)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && Modules.AddOns.Contains((string)f.GetRawConstantValue()!))
            .ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue()!);

        [Fact]
        public void EveryAddOn_HasAFolderName()
            => Assert.Equal(Modules.AddOns.Order(), AddOnFolders.Values.Order());

        [Fact]
        public void ControllersInAnAddOnFolder_RequireTheirModule()
        {
            var missing = Missing(typeof(ApiController).Assembly.GetTypes());

            Assert.True(missing.Count == 0,
                $"Add-on controllers must carry [RequiresModule] (docs/v2/adr/0008 decision 9):{Environment.NewLine}{string.Join(Environment.NewLine, missing)}");
        }

        [Fact]
        public void Missing_FindsAnActionWithoutItsModule()
        {
            // Guards the scan itself, against planted controllers in a Billing folder of this assembly
            var missing = Missing([typeof(Fakes.Controllers.V2.Billing.UnguardedController),
                typeof(Fakes.Controllers.V2.Billing.GuardedController), typeof(Fakes.Controllers.V2.Billing.WrongModuleController),
                typeof(Fakes.Controllers.V2.Billing.GuardedActionController)]);

            Assert.Equal(
                [$"{typeof(Fakes.Controllers.V2.Billing.GuardedActionController).FullName}.Unguarded needs [RequiresModule(Modules.Billing)]",
                 $"{typeof(Fakes.Controllers.V2.Billing.UnguardedController).FullName}.Get needs [RequiresModule(Modules.Billing)]",
                 $"{typeof(Fakes.Controllers.V2.Billing.WrongModuleController).FullName}.Get needs [RequiresModule(Modules.Billing)]"],
                missing);
        }

        [Fact]
        public void RequiresModule_UnknownModule_Throws()
            // A typo must not quietly lock or open a controller
            => Assert.Throws<ArgumentException>(() => new RequiresModuleAttribute("BILL"));

        // "Controller.Action needs [RequiresModule(Modules.X)]" for every action in an add-on folder without it, sorted
        private static List<string> Missing(IEnumerable<Type> types)
        {
            var missing = new List<string>();
            foreach (var controller in types.Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract))
            {
                var folders = (controller.Namespace ?? "").Split('.').Where(AddOnFolders.ContainsKey);
                foreach (var folder in folders)
                {
                    var module = AddOnFolders[folder];
                    var onController = controller.GetCustomAttributes<RequiresModuleAttribute>(inherit: true).Any(a => a.Module == module);
                    var actions = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                        .Where(m => !m.IsSpecialName && m.GetCustomAttribute<NonActionAttribute>() is null);

                    foreach (var action in actions)
                        if (!onController && !action.GetCustomAttributes<RequiresModuleAttribute>().Any(a => a.Module == module))
                            missing.Add($"{controller.FullName}.{action.Name} needs [RequiresModule(Modules.{folder})]");
                }
            }

            return [.. missing.Order(StringComparer.Ordinal)];
        }
    }
}

// Planted controllers for Missing_FindsAnActionWithoutItsModule: their namespace puts them in the Billing folder
namespace ClinicFlow.Tests.Architecture.Fakes.Controllers.V2.Billing
{
    public class UnguardedController : ApiController
    {
        public IActionResult Get() => Ok();
    }

    [RequiresModule(Modules.Billing)]
    public class GuardedController : ApiController
    {
        public IActionResult Get() => Ok();
    }

    [RequiresModule(Modules.Insurance)]
    public class WrongModuleController : ApiController
    {
        public IActionResult Get() => Ok();
    }

    public class GuardedActionController : ApiController
    {
        [RequiresModule(Modules.Billing)]
        public IActionResult Guarded() => Ok();

        public IActionResult Unguarded() => Ok();
    }
}
