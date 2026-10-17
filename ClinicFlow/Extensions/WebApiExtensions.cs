using FluentValidation;
using Microsoft.AspNetCore.HttpOverrides;
using Presentation.Filters;
using System.Text.Json.Serialization;

namespace ClinicFlow.Extensions
{
    public static class WebApiExtensions
    {
        public static IServiceCollection AddWebApiServices(this IServiceCollection services)
        {
            // Register Web API services, controllers, etc.
            services.AddScoped<ValidationFilter>();
            services.AddControllers(options =>
            {
                options.Filters.AddService<ValidationFilter>();
            })
                .AddApplicationPart(typeof(Presentation.Controllers.ApiController).Assembly)
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.Converters.Add(
                        new JsonStringEnumConverter());
                });
            services.AddEndpointsApiExplorer();

            // Every error response is ProblemDetails with a traceId: unhandled exceptions (UseExceptionHandler),
            // bodiless 401/403/404 (UseStatusCodePages) and validation failures (REVIEW.md M5, NL9)
            services.AddProblemDetails();
            services.AddValidatorsFromAssembly(typeof(ValidationFilter).Assembly);

            // Client IP comes from Connection.RemoteIpAddress; X-Forwarded-For is honoured
            // only from known proxies (loopback by default).
            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            });

            return services;
        }
    }
}
