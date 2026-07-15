namespace Presentation.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ApiController : ControllerBase
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

        // Central error dispatcher
        private ActionResult HandleProblem(IReadOnlyList<Error> errors)
        {
            if (errors.Count == 0)
                return Problem(
                    title: "An unexpected error occurred",
                    statusCode: StatusCodes.Status500InternalServerError);

            if (errors.All(e => e.Type == ErrorType.Validation))
                return HandleValidationProblem(errors);

            return HandleSingleErrorProblem(errors[0]);
        }

        private ActionResult HandleSingleErrorProblem(Error error)
        {
            return Problem(
                title: error.Code,
                detail: error.Description,
                statusCode: GetStatusCode(error.Type));
        }

        private static int GetStatusCode(ErrorType errorType) => errorType switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.InvalidCredentials => StatusCodes.Status401Unauthorized,
            ErrorType.Failure => StatusCodes.Status500InternalServerError,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };

        private ActionResult HandleValidationProblem(IReadOnlyList<Error> errors)
        {
            var modelState = new ModelStateDictionary();

            foreach (var error in errors)
                modelState.AddModelError(error.Code, error.Description);

            return ValidationProblem(modelState);
        }
    }
}
