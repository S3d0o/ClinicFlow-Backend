using FluentValidation;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Presentation;
using Presentation.Filters;
using Shared.Settings;
using System.Globalization;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

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

        // A per-client-IP fixed window on the credential and email endpoints (REVIEW.md M6). Lockout only protects
        // one account; this also slows credential stuffing across accounts and email flooding.
        // A rejected request gets 429 ProblemDetails with the code RateLimit.Exceeded and a Retry-After header.
        public static IServiceCollection AddAuthRateLimiting(this IServiceCollection services, IConfiguration config)
        {
            services.AddOptions<RateLimitSettings>()
                .Bind(config.GetSection("RateLimiting:Auth"))
                .Validate(s => s.PermitLimit > 0 && s.WindowSeconds > 0,
                    "RateLimiting:Auth:PermitLimit and WindowSeconds must be greater than 0.")
                .ValidateOnStart();

            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                options.AddPolicy(RateLimitPolicies.Auth, httpContext =>
                {
                    var settings = httpContext.RequestServices.GetRequiredService<IOptions<RateLimitSettings>>().Value;
                    // RemoteIpAddress already reflects X-Forwarded-For from trusted proxies (UseForwardedHeaders)
                    var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                    return RateLimitPartition.GetFixedWindowLimiter(clientIp, _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = settings.PermitLimit,
                        Window = TimeSpan.FromSeconds(settings.WindowSeconds),
                        QueueLimit = 0
                    });
                });

                options.OnRejected = async (context, ct) =>
                {
                    var response = context.HttpContext.Response;
                    if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                        response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

                    var problemDetails = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
                    await problemDetails.WriteAsync(new ProblemDetailsContext
                    {
                        HttpContext = context.HttpContext,
                        ProblemDetails =
                        {
                            Status = StatusCodes.Status429TooManyRequests,
                            Title = "RateLimit.Exceeded",
                            Detail = "Too many requests. Wait a moment and try again."
                        }
                    });
                };
            });

            return services;
        }
    }
}
