using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Presentation.Extensions;
using MvcJsonOptions = Microsoft.AspNetCore.Mvc.JsonOptions;

namespace Presentation.Serialization
{
    // JSON formatters that apply only to /api/v2 actions (HttpContext.IsApiV2), with ApiJson.CreateV2Options. They sit in
    // front of MVC's own JSON formatters, which keep serving every inherited route unchanged. Responses written by these
    // include ProblemDetails, so an error from a V2 action is formatted the same way.
    // They report no content types to ApiExplorer: MVC's JSON formatters already describe the same media types, and
    // listing them twice would only duplicate the OpenAPI document's entries
    public sealed class V2JsonInputFormatter(MvcJsonOptions options, ILogger<SystemTextJsonInputFormatter> logger)
        : SystemTextJsonInputFormatter(options, logger)
    {
        public override bool CanRead(InputFormatterContext context)
            => context.HttpContext.IsApiV2() && base.CanRead(context);

        public override IReadOnlyList<string>? GetSupportedContentTypes(string? contentType, Type objectType) => null;
    }

    public sealed class V2JsonOutputFormatter(MvcJsonOptions options)
        : SystemTextJsonOutputFormatter(options.JsonSerializerOptions)
    {
        public override bool CanWriteResult(OutputFormatterCanWriteContext context)
            => context.HttpContext.IsApiV2() && base.CanWriteResult(context);

        public override IReadOnlyList<string>? GetSupportedContentTypes(string? contentType, Type objectType) => null;
    }

    // Registered by the host (AddWebApiServices) after AddControllers, so it runs after MVC's own setup and inserting at 0
    // puts these formatters first
    public sealed class V2JsonMvcOptionsSetup(IOptions<MvcJsonOptions> jsonOptions, ILoggerFactory loggerFactory)
        : IConfigureOptions<MvcOptions>
    {
        public void Configure(MvcOptions options)
        {
            var v2 = ApiJson.CreateV2Options(jsonOptions.Value);
            options.InputFormatters.Insert(0, new V2JsonInputFormatter(v2, loggerFactory.CreateLogger<SystemTextJsonInputFormatter>()));
            options.OutputFormatters.Insert(0, new V2JsonOutputFormatter(v2));
        }
    }
}
