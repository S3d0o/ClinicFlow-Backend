using Domain.Enums;
using Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Services.Abstraction.Contracts;
using Shared.DTOs.Notification;

namespace ClinicFlow.BackgroundJobs;

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

    private async Task ProcessRemindersAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();

            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var notificationService =
                scope.ServiceProvider.GetRequiredService<INotificationService>();
            var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

            var appointments =
                await uow.Appointments.GetAppointmentsNeedingReminderAsync(cancellationToken);

            if (appointments.Count == 0)
            {
                logger.LogInformation("No appointment reminders to send.");
                return;
            }

            foreach (var appointment in appointments)
            {
                try
                {
                    await notificationService.CreateAsync(
                        new CreateNotificationRequest(
                            UserId: appointment.Patient.UserId,
                            Title: "Appointment Reminder",
                            Message:
                                $"Reminder: You have an appointment tomorrow at {appointment.Slot.StartTime:HH:mm}.",
                            Type: NotificationType.AppointmentReminder,
                            RelatedEntityId: appointment.Id), cancellationToken);

                    appointment.ReminderSentAt = DateTime.UtcNow;

                    await emailService.SendAppointmentReminderAsync(
                        email: appointment.Patient.User.Email!, // patient + user included in query
                        patientName: $"{appointment.Patient.User.FirstName} {appointment.Patient.User.LastName}",
                        doctorName: $"{appointment.Slot.DoctorProfile.User.FirstName} {appointment.Slot.DoctorProfile.User.LastName}",
                        date: appointment.Slot.Date,
                        time: appointment.Slot.StartTime);
                    logger.LogInformation(
                        "Reminder sent for Appointment {AppointmentId} to Patient {PatientId}.",
                        appointment.Id,
                        appointment.Patient.UserId);
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Failed to send reminder for Appointment {AppointmentId}.",
                        appointment.Id);
                }
            }

            await uow.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                 "Appointment reminder job completed. {Count} reminders processed.",
                 appointments.Count);
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