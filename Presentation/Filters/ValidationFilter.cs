// Presentation/Filters/ValidationFilter.cs
using FluentValidation;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace Presentation.Filters;

// Async so validators may use MustAsync/CustomAsync; errors from every argument are reported together.
public class ValidationFilter(ILogger<ValidationFilter> logger) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var modelState = new ModelStateDictionary();
        var ct = context.HttpContext.RequestAborted;

        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null) continue;

            var type = argument.GetType();
            if (type.IsPrimitive || type == typeof(string)) continue;

            logger.LogDebug("Validating request model of type {ModelType}", type.Name);

            var validator = context.HttpContext.RequestServices
                .GetService(typeof(IValidator<>).MakeGenericType(type));

            if (validator is not IValidator v)
            {
                logger.LogDebug("No validator found for {ModelType}", type.Name);
                continue;
            }

            var result = await v.ValidateAsync(new ValidationContext<object>(argument), ct);
            if (result.IsValid) continue;

            var errors = result.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(e => e.ErrorMessage).ToArray()
                );

            logger.LogWarning(
                "Validation failed for {ModelType}. Errors: {@Errors}",
                type.Name,
                errors);

            foreach (var (key, messages) in errors)
                foreach (var message in messages)
                    modelState.AddModelError(key, message);
        }

        if (modelState.ErrorCount > 0)
        {
            context.Result = new BadRequestObjectResult(
                new ValidationProblemDetails(modelState));
            return;
        }

        await next();
    }
}
