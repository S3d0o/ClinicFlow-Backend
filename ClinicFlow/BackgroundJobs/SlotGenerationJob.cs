using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Services.Abstraction.Contracts;

namespace ClinicFlow.BackgroundJobs;

// Slots are only generated 14 days ahead, so without this job a schedule set up once runs dry
// two weeks later (REVIEW.md H8). Generation is idempotent: existing slots are skipped
public sealed class SlotGenerationJob(
    IServiceScopeFactory scopeFactory,
    ILogger<SlotGenerationJob> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Slot Generation Background Job started.");

        // Run once immediately when the application starts.
        await GenerateSlotsAsync(stoppingToken);

        using var timer = new PeriodicTimer(Interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await GenerateSlotsAsync(stoppingToken);
        }

        logger.LogInformation("Slot Generation Background Job stopped.");
    }

    // internal so integration tests can run one pass without waiting for the daily timer
    internal async Task GenerateSlotsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var doctorService = scope.ServiceProvider.GetRequiredService<IDoctorService>();

            var created = await doctorService.GenerateUpcomingSlotsAsync(cancellationToken);

            logger.LogInformation("Slot generation job completed. {Count} slots created.", created);
        }
        catch (OperationCanceledException)
        {
            // Expected during application shutdown.
            logger.LogInformation("Slot generation job was cancelled.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while generating slots.");
        }
    }
}
