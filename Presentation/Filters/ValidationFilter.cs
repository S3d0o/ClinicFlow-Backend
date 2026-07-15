// Presentation/Filters/ValidationFilter.cs
using FluentValidation;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace Presentation.Filters;

public class ValidationFilter(ILogger<ValidationFilter> logger) : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
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

            var result = v.Validate(new ValidationContext<object>(argument));
            if (!result.IsValid)
            {
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

                var modelState = new ModelStateDictionary();
                foreach (var (key, messages) in errors)
                    foreach (var message in messages)
                        modelState.AddModelError(key, message);

                context.Result = new BadRequestObjectResult(
                    new ValidationProblemDetails(modelState));
            }
        }
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}