using Domain.ResultPattern;
using FluentValidation;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Presentation;
using Presentation.Extensions;
using Presentation.Filters;
using Presentation.Serialization;
using Services.Abstraction.Contracts;
using Shared.Settings;
using System.Globalization;
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
                // Every JSON setting lives in ApiJson.Configure, which the /api/v2 options start from too
                .AddJsonOptions(options => ApiJson.Configure(options.JsonSerializerOptions));
            // /api/v2 actions get their own JSON formatters: ids as strings, "HH:mm" times (contract §13.1)
            services.AddTransient<IConfigureOptions<MvcOptions>, V2JsonMvcOptionsSetup>();
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
                    var hasRetryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter);
                    if (hasRetryAfter)
                        response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

                    var problem = new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "RateLimit.Exceeded",
                        Detail = "Too many requests. Wait a moment and try again."
                    };

                    // /api/v2 also gives the instant as retryAt (contract §13.1 "Rate limits", §13.10)
                    if (hasRetryAfter && context.HttpContext.IsApiV2())
                    {
                        var clock = context.HttpContext.RequestServices.GetRequiredService<IClock>();
                        problem.Extensions[ErrorMetadata.RetryAt] = clock.UtcNow.Add(retryAfter);
                    }

                    var problemDetails = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
                    await problemDetails.WriteAsync(new ProblemDetailsContext
                    {
                        HttpContext = context.HttpContext,
                        ProblemDetails = problem
                    });
                };
            });

            return services;
        }
    }
}
