// Presentation/Filters/ValidationFilter.cs
using FluentValidation;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Presentation.Filters;

// Async so validators may use MustAsync/CustomAsync; errors from every argument are reported together.
// Keys are camelCase property paths (Email → email, Lines[0].UnitPrice → lines[0].unitPrice) on every route
// (P1 plan Q-F, contract §3.1 B), so they match the JSON names the client sent.
// An argument is checked by its type's validator and by the validators of its base types, so a list filter derived
// from PageRequest gets the shared paging rule without including it.
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

            var validators = ValidatorsFor(type, context.HttpContext.RequestServices);
            if (validators.Count == 0)
            {
                logger.LogDebug("No validator found for {ModelType}", type.Name);
                continue;
            }

            var failures = new List<FluentValidation.Results.ValidationFailure>();
            foreach (var validator in validators)
            {
                var result = await validator.ValidateAsync(new ValidationContext<object>(argument), ct);
                failures.AddRange(result.Errors);
            }

            if (failures.Count == 0) continue;

            var errors = failures
                .GroupBy(e => ValidationKeys.ToCamelCase(e.PropertyName))
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(e => e.ErrorMessage).Distinct().ToArray()
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
            // Same factory as ValidationProblem() and [ApiController]'s binding errors, so every 400
            // has one shape: type, title, status, errors and traceId (REVIEW.md NL9)
            var problemDetailsFactory = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
            context.Result = new BadRequestObjectResult(
                problemDetailsFactory.CreateValidationProblemDetails(context.HttpContext, modelState));
            return;
        }

        await next();
    }

    // The type's own validator first, then those of its base types (object excluded)
    private static List<IValidator> ValidatorsFor(Type type, IServiceProvider services)
    {
        var validators = new List<IValidator>();
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            if (services.GetService(typeof(IValidator<>).MakeGenericType(current)) is IValidator validator)
                validators.Add(validator);
        }

        return validators;
    }
}
