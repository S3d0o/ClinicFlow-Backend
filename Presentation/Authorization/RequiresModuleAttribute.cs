using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Services.Abstraction.Contracts;
using Shared.Errors;

namespace Presentation.Authorization
{
    // The second line of defence on an add-on module's controllers (docs/v2/adr/0008 decision 9):
    // [RequiresModule(Modules.Billing)] answers 403 Subscription.ModuleNotEnabled while the module is off for the
    // organization. [HasPermission] already checks the permission's module; this also covers actions that need only a
    // signed-in user (patient and applicant routes). A resource filter, so it runs after authentication and
    // authorization but before model binding and validation. RequiresModuleTests fail when a controller in an add-on
    // module's folder lacks it
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public sealed class RequiresModuleAttribute : Attribute, IAsyncResourceFilter
    {
        public RequiresModuleAttribute(string module)
        {
            // A typo must not quietly lock (or open) a controller
            if (!Modules.All.Contains(module))
                throw new ArgumentException($"Unknown module '{module}' in [RequiresModule]", nameof(module));
            Module = module;
        }

        public string Module { get; }

        public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
        {
            var http = context.HttpContext;
            var entitlements = http.RequestServices.GetRequiredService<IEntitlements>();
            if (await entitlements.IsEnabledAsync(Module, http.RequestAborted))
            {
                await next();
                return;
            }

            http.RequestServices.GetRequiredService<ILogger<RequiresModuleAttribute>>()
                .LogWarning("Refused {Path}: the {Module} module is not enabled for the organization", http.Request.Path, Module);

            // The same ProblemDetails as ApiController.HandleResult gives a Forbidden error
            var error = SubscriptionErrors.ModuleNotEnabled(Module);
            var problem = http.RequestServices.GetRequiredService<ProblemDetailsFactory>()
                .CreateProblemDetails(http, StatusCodes.Status403Forbidden, title: error.Code, detail: error.Description);
            context.Result = new ObjectResult(problem) { StatusCode = problem.Status };
        }
    }
}
