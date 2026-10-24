using Microsoft.Extensions.Diagnostics.HealthChecks;
using Persistence.AppData;

namespace ClinicFlow.HealthChecks;

// Ready only while the database answers: without it no request can be served (docs/v2/06 §14, REVIEW.md M12)
public sealed class DatabaseHealthCheck(ClinicDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        => await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("The database can't be reached.");
}
