using Shared.DTOs.Common;
using System.Globalization;

namespace Presentation.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public abstract class ApiController : ControllerBase
    {
        // Non-generic: use for commands that return no value.
        // Default is 204 NoContent. Pass Status200OK or Status201Created when needed.
        protected IActionResult HandleResult(
            Result result,
            int successStatusCode = StatusCodes.Status204NoContent)
        {
            if (result.IsSuccess)
                return StatusCode(successStatusCode);

            return HandleProblem(result.Errors);
        }

        // Generic: use when the service returns a value.
        // Pass locationUri for POST endpoints that create resources → 201 Created.
        // Omit locationUri for GET/PUT → 200 OK.
        protected ActionResult<TValue> HandleResult<TValue>(
            Result<TValue> result,
            string? locationUri = null)
        {
            if (result.IsSuccess)
                return locationUri is not null
                    ? Created(locationUri, result.Value)
                    : Ok(result.Value);

            return HandleProblem(result.Errors);
        }

        // POST endpoints that need 201 Created with body but no location URI yet
        protected ActionResult<TValue> HandleResult<TValue>(
            Result<TValue> result,
            int successStatusCode)
        {
            if (result.IsSuccess)
                return StatusCode(successStatusCode, result.Value);

            return HandleProblem(result.Errors);
        }

        // Paged lists: the body stays a plain array and the total goes in the X-Total-Count header (REVIEW.md NL2)
        public const string TotalCountHeader = "X-Total-Count";

        protected ActionResult<IReadOnlyList<TItem>> HandlePagedResult<TItem>(Result<PagedList<TItem>> result)
        {
            if (result.IsFailure)
                return HandleProblem(result.Errors);

            Response.Headers[TotalCountHeader] = result.Value.TotalCount.ToString(CultureInfo.InvariantCulture);
            return Ok(result.Value.Items);
        }

        // Inherited routes send business-rule validation errors as shape C (the code as a key of "errors",
        // contract §3.1); /api/v2 sends every coded error as shape A (V2ApiController, contract §13.1)
        protected virtual bool CodedValidationErrorsAsProblemDetails => false;

        protected virtual int StatusCodeFor(ErrorType errorType) => errorType switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.InvalidCredentials => StatusCodes.Status401Unauthorized,
            ErrorType.Failure => StatusCodes.Status500InternalServerError,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.TooManyRequests => StatusCodes.Status429TooManyRequests,
            _ => StatusCodes.Status500InternalServerError
        };

        // Central error dispatcher
        private ActionResult HandleProblem(IReadOnlyList<Error> errors)
        {
            if (errors.Count == 0)
                return Problem(
                    title: "An unexpected error occurred",
                    statusCode: StatusCodes.Status500InternalServerError);

            if (!CodedValidationErrorsAsProblemDetails && errors.All(e => e.Type == ErrorType.Validation))
                return HandleValidationProblem(errors);

            return HandleCodedProblem(errors);
        }

        // Shape A: title is the code of the first error, and its metadata (retryAt, attemptsLeft, ...) become top-level
        // members of the ProblemDetails. Several validation errors (/api/v2 only) share the status, so their messages are
        // joined into the detail
        private ActionResult HandleCodedProblem(IReadOnlyList<Error> errors)
        {
            var error = errors[0];
            var detail = errors.Count > 1 && errors.All(e => e.Type == ErrorType.Validation)
                ? string.Join(" ", errors.Select(e => e.Description).Distinct())
                : error.Description;

            var problem = ProblemDetailsFactory.CreateProblemDetails(
                HttpContext,
                statusCode: StatusCodeFor(error.Type),
                title: error.Code,
                detail: detail);

            foreach (var (key, value) in error.Metadata)
                problem.Extensions[key] = value;

            return new ObjectResult(problem) { StatusCode = problem.Status };
        }

        private ActionResult HandleValidationProblem(IReadOnlyList<Error> errors)
        {
            var modelState = new ModelStateDictionary();

            foreach (var error in errors)
                modelState.AddModelError(error.Code, error.Description);

            return ValidationProblem(modelState);
        }
    }
}
