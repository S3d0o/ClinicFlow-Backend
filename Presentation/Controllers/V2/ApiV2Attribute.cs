namespace Presentation.Controllers.V2
{
    // Endpoint metadata that marks an /api/v2 action, so the parts of the pipeline that differ for V2 (the JSON formatters,
    // the rate limiter's retryAt) can tell it apart: HttpContext.IsApiV2(). Carried by V2ApiController; don't use it directly
    [AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
    public sealed class ApiV2Attribute : Attribute
    {
    }
}
