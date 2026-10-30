namespace Presentation.Controllers.V2
{
    // Base class of every /api/v2 controller (docs/v2/adr/0005, API_CONTRACT.md §13.1). A V2 controller lives in this
    // folder, inherits this class and sets its route under the prefix: [Route(RoutePrefix + "/branches")].
    // What it changes compared with the inherited routes:
    // - every error with a code is ProblemDetails with title = the code (shape A), 400 included: no shape C
    // - Auth.InvalidCredentials-type errors are 400, so 401 only ever means a token problem (§13.1 "401")
    // - JSON (V2JsonFormatters, through ApiV2Attribute): ids are strings and times are "HH:mm"
    // The rest is shared with ApiController: HandleResult / HandlePagedResult, error metadata as ProblemDetails
    // extensions, camelCase validation keys (ValidationFilter) and PageRequest for paged lists.
    // Authorization is explicit on every action as everywhere else ([HasPermission] for new endpoints, docs/v2/adr/0002)
    [ApiV2]
    [Route(RoutePrefix + "/[controller]")]
    public abstract class V2ApiController : ApiController
    {
        public const string RoutePrefix = "api/v2";

        protected override bool CodedValidationErrorsAsProblemDetails => true;

        protected override int StatusCodeFor(ErrorType errorType) => errorType switch
        {
            ErrorType.InvalidCredentials => StatusCodes.Status400BadRequest,
            _ => base.StatusCodeFor(errorType)
        };
    }
}
