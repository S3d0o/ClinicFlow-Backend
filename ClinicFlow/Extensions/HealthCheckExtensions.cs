using ClinicFlow.HealthChecks;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Text.Json;

namespace ClinicFlow.Extensions;

// The probes (docs/v2/06 §14, REVIEW.md M12). Live: the process answers, so an orchestrator restarts it only when it
// hangs, never because the database is down. Ready: it can serve (the database) and keeps up (the outbox lag), so a
// load balancer sends it traffic. All anonymous, and the ready body names checks and statuses only
public static class HealthCheckExtensions
{
    private const string ReadyTag = "ready";

    public static IServiceCollection AddHealthProbes(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", tags: [ReadyTag], timeout: TimeSpan.FromSeconds(5))
            .AddCheck<OutboxHealthCheck>("outbox", tags: [ReadyTag], timeout: TimeSpan.FromSeconds(5));
        return services;
    }

    public static WebApplication MapHealthProbes(this WebApplication app)
    {
        var live = new HealthCheckOptions { Predicate = _ => false };
        app.MapHealthChecks("/health/live", live).AllowAnonymous(); // the fallback policy would otherwise require a login
        app.MapHealthChecks("/health", live).AllowAnonymous();      // V1's route, kept for older compose files and images

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ReadyTag),
            ResponseWriter = WriteReadyAsync,
        }).AllowAnonymous();

        return app;
    }

    // {"status":"Healthy","checks":{"database":{"status":"Healthy"},"outbox":{"status":"Healthy","lagSeconds":0,…}}}.
    // Degraded is still 200; Unhealthy is 503. A check that threw shows no exception text: the route is anonymous
    private static Task WriteReadyAsync(HttpContext context, HealthReport report)
    {
        var checks = new Dictionary<string, Dictionary<string, object>>();
        foreach (var (name, entry) in report.Entries)
        {
            var check = new Dictionary<string, object> { ["status"] = entry.Status.ToString() };
            if ((entry.Exception is null ? entry.Description : "The check failed.") is { } description)
                check["description"] = description;
            foreach (var (key, value) in entry.Data)
                check[key] = value;
            checks[name] = check;
        }

        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(JsonSerializer.Serialize(new { status = report.Status.ToString(), checks }));
    }
}
