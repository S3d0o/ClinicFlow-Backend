using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.DependencyInjection;
using Shared.Errors;

namespace Presentation.Authorization
{
    // A permission refused only because its module is off (ModuleNotEnabledReason) answers 403 with the code
    // Subscription.ModuleNotEnabled, so the frontend can say "not in your plan" (docs/v2/adr/0008 decision 10).
    // Every other result is handled as before, by the framework's default handler
    public sealed class SubscriptionAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
    {
        private readonly AuthorizationMiddlewareResultHandler _default = new();

        public async Task HandleAsync(
            RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
        {
            if (!authorizeResult.Forbidden
                || authorizeResult.AuthorizationFailure?.FailureReasons.OfType<ModuleNotEnabledReason>().FirstOrDefault() is not { } reason)
            {
                await _default.HandleAsync(next, context, policy, authorizeResult);
                return;
            }

            // TryWrite: a client that accepts no JSON gets the bare 403 (WriteAsync would throw), which the status code
            // pages middleware then answers as for any other 403
            var error = SubscriptionErrors.ModuleNotEnabled(reason.Module);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.RequestServices.GetRequiredService<IProblemDetailsService>().TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails =
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = error.Code,
                    Detail = error.Description,
                },
            });
        }
    }
}
