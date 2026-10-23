using Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Services.Abstraction.Contracts;

namespace ClinicFlow.BackgroundJobs;

// Hourly: every confirmed visit starting within 24 hours that hasn't been reminded is marked as reminded, and its
// AppointmentReminderDue event goes into the outbox in the same save. AppointmentReminderDueHandler sends the email
// and the notification, and the outbox retries a failed email (docs/v2/06 §5)
public sealed class AppointmentReminderJob(
    IServiceScopeFactory scopeFactory,
    ILogger<AppointmentReminderJob> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Appointment Reminder Background Job started.");

        // Run once immediately when the application starts.
        await ProcessRemindersAsync(stoppingToken);

        using var timer = new PeriodicTimer(Interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ProcessRemindersAsync(stoppingToken);
        }

        logger.LogInformation("Appointment Reminder Background Job stopped.");
    }

    // internal so integration tests can run one pass without waiting for the hourly timer
    internal async Task ProcessRemindersAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();

            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();

            var appointments =
                await uow.Appointments.GetAppointmentsNeedingReminderAsync(clock.UtcNow, cancellationToken);

            if (appointments.Count == 0)
            {
                logger.LogInformation("No appointment reminders to send.");
                return;
            }

            foreach (var appointment in appointments)
                appointment.QueueReminder(clock.UtcNow);

            // The ReminderSentAt marks and their events are saved together: each reminder is queued exactly once
            await uow.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Appointment reminder job queued {Count} reminders.", appointments.Count);
        }
        catch (OperationCanceledException)
        {
            // Expected during application shutdown.
            logger.LogInformation("Appointment reminder job was cancelled.");
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "An unexpected error occurred while processing appointment reminders.");
        }
    }
}
