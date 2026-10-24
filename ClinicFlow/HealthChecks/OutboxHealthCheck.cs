using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Persistence.AppData;
using Services.Abstraction.Contracts;

namespace ClinicFlow.HealthChecks;

// How far the outbox has fallen behind (docs/v2/06 §5, §14): the wait of the oldest message that is due. The dispatcher
// runs every 2 s, so a long wait means notifications and emails are stuck. That is Degraded, not Unhealthy: the API
// still serves requests, so the instance stays in rotation while someone looks
public sealed class OutboxHealthCheck(ClinicDbContext db, IClock clock) : IHealthCheck
{
    public static readonly TimeSpan MaxLag = TimeSpan.FromMinutes(1);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;

        // Every organization's messages, as the dispatcher delivers them all. A message waiting out a retry's
        // backoff isn't due, so it isn't lag
        var waiting = db.OutboxMessages.IgnoreQueryFilters()
            .Where(m => m.ProcessedAtUtc == null && m.DeadLetteredAtUtc == null);
        var oldestDue = await waiting
            .Where(m => m.NextAttemptAtUtc == null || m.NextAttemptAtUtc <= now)
            .MinAsync(m => (DateTime?)(m.NextAttemptAtUtc ?? m.OccurredAtUtc), cancellationToken);
        var deadLettered = await db.OutboxMessages.IgnoreQueryFilters()
            .CountAsync(m => m.DeadLetteredAtUtc != null, cancellationToken);

        var lag = oldestDue is { } due && due < now ? now - due : TimeSpan.Zero;
        var data = new Dictionary<string, object>
        {
            ["lagSeconds"] = (int)lag.TotalSeconds,
            ["deadLettered"] = deadLettered,
        };

        return lag > MaxLag
            ? HealthCheckResult.Degraded($"The oldest due message has waited {(int)lag.TotalSeconds} s.", data: data)
            : HealthCheckResult.Healthy(data: data);
    }
}
