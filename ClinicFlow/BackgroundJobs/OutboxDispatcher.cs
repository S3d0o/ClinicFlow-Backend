using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Persistence.AppData;
using Persistence.Outbox;

namespace ClinicFlow.BackgroundJobs;

// Delivers the outbox every 2 s (docs/v2/06 §5, §7). Only one instance dispatches at a time: each pass takes a SQL
// Server application lock first and skips the pass if another instance holds it. The claim query's UPDLOCK/READPAST
// would keep two dispatchers off the same rows anyway; the lock keeps them from competing at all
public sealed class OutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    OutboxProcessor processor,
    ILogger<OutboxDispatcher> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Outbox Dispatcher started.");

        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await DispatchAsync(stoppingToken);

        logger.LogInformation("Outbox Dispatcher stopped.");
    }

    internal async Task DispatchAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ClinicDbContext>();

            // A session lock lives as long as this connection, so it stays open for the whole pass
            await db.Database.OpenConnectionAsync(ct);
            var locked = false;
            try
            {
                var result = await db.Database.SqlQuery<int>($"""
                    DECLARE @result int;
                    EXEC @result = sp_getapplock @Resource = N'ClinicFlow.OutboxDispatcher', @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0;
                    SELECT @result AS Value
                    """).ToListAsync(ct);
                locked = result.Single() >= 0;
                if (!locked)
                    return; // another instance is dispatching

                // Drain what is due, a batch at a time
                while (await processor.ProcessBatchAsync(ct) == OutboxProcessor.BatchSize) { }
            }
            finally
            {
                // Released explicitly: a pooled connection keeps its session, and with it the lock
                if (locked)
                    await db.Database.ExecuteSqlAsync($"EXEC sp_releaseapplock @Resource = N'ClinicFlow.OutboxDispatcher', @LockOwner = 'Session'", CancellationToken.None);
                await db.Database.CloseConnectionAsync();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutting down
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Outbox dispatch pass failed");
        }
    }
}
