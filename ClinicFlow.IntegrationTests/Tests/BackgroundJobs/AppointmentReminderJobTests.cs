using Domain.Entities.Tenancy;
using ClinicFlow.BackgroundJobs;
using ClinicFlow.Domain.Enums;
using ClinicFlow.IntegrationTests.Infrastructure;
using Domain.Entities.AppModule;
using Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Persistence.AppData;

namespace ClinicFlow.IntegrationTests.Tests.BackgroundJobs
{
    // Runs single passes of the hourly job against SQL Server. The 24-hour window is computed from
    // the clinic's clock (ClinicNow), so appointments are placed relative to it (REVIEW.md M2)
    [Collection(nameof(IntegrationTestCollection))]
    public class AppointmentReminderJobTests : IAsyncLifetime
    {
        private int _doctorId;
        private int _scheduleId;

        private readonly ClinicFlowApiFactory _factory;
        private readonly AppointmentReminderJob _job;

        public AppointmentReminderJobTests(ClinicFlowApiFactory factory)
        {
            _factory = factory;
            _job = new AppointmentReminderJob(
                factory.Services.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<AppointmentReminderJob>.Instance);
        }

        public async Task InitializeAsync()
        {
            await _factory.ResetDataBaseAsync();
            await _factory.SeedAsync(async db =>
            {
                (_, _doctorId) = await SeedDataHelper.SeedDoctorAsync(db, "reminders");
                (_scheduleId, _) = await SeedDataHelper.SeedScheduleWithSlotsAsync(db, _doctorId, DayOfWeek.Monday);
            });
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task Run_RemindsOnlyConfirmedUnremindedAppointmentsInTheNext24Hours()
        {
            int dueId = 0;
            var notDue = new List<int>();
            await _factory.SeedAsync(async db =>
            {
                dueId = await AddAppointmentAsync(db, "due", hoursFromNow: 3);
                notDue.Add(await AddAppointmentAsync(db, "later", hoursFromNow: 30));
                notDue.Add(await AddAppointmentAsync(db, "past", hoursFromNow: -2));
                notDue.Add(await AddAppointmentAsync(db, "cancelled", hoursFromNow: 5, AppointmentStatus.Cancelled));
                await AddAppointmentAsync(db, "reminded", hoursFromNow: 6, alreadyReminded: true);
            });

            await _job.ProcessRemindersAsync(CancellationToken.None);

            _factory.Emails.Sent.Should().ContainSingle()
                .Which.Should().Be(new SentEmail(SentEmailKind.AppointmentReminder, "patientdue@test.com"));
            await _factory.SeedAsync(async db =>
            {
                (await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == dueId)).ReminderSentAt.Should().NotBeNull();
                (await db.Notifications.AsNoTracking().Where(n => n.Type == NotificationType.AppointmentReminder)
                    .Select(n => n.RelatedEntityId).ToListAsync()).Should().Equal(dueId);
                (await db.Appointments.AsNoTracking().Where(a => notDue.Contains(a.Id)).Select(a => a.ReminderSentAt).ToListAsync())
                    .Should().HaveCount(3).And.OnlyContain(sentAt => sentAt == null);
            });
        }

        [Fact]
        public async Task Run_Twice_SendsEachReminderOnce()
        {
            await _factory.SeedAsync(async db => await AddAppointmentAsync(db, "once", hoursFromNow: 4));

            await _job.ProcessRemindersAsync(CancellationToken.None);
            await _job.ProcessRemindersAsync(CancellationToken.None);

            _factory.Emails.Sent.Should().ContainSingle();
            await _factory.SeedAsync(async db =>
                (await db.Notifications.CountAsync(n => n.Type == NotificationType.AppointmentReminder)).Should().Be(1));
        }

        [Fact]
        public async Task Run_WhenOneEmailFails_RemindsTheOthersAndRetriesTheFailedOneNextRun()
        {
            int failingId = 0, okId = 0;
            await _factory.SeedAsync(async db =>
            {
                failingId = await AddAppointmentAsync(db, "bounce", hoursFromNow: 3);
                okId = await AddAppointmentAsync(db, "fine", hoursFromNow: 4);
            });
            _factory.Emails.FailFor("patientbounce@test.com");

            await _job.ProcessRemindersAsync(CancellationToken.None);

            _factory.Emails.Sent.Select(e => e.To).Should().Equal("patientfine@test.com");
            await _factory.SeedAsync(async db =>
            {
                (await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == okId)).ReminderSentAt.Should().NotBeNull();
                // Email first: a failed send leaves the reminder unstamped and without a notification
                (await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == failingId)).ReminderSentAt.Should().BeNull();
                (await db.Notifications.AnyAsync(n => n.RelatedEntityId == failingId)).Should().BeFalse();
            });

            _factory.Emails.Clear(); // the mailbox recovers
            await _job.ProcessRemindersAsync(CancellationToken.None);

            _factory.Emails.Sent.Select(e => e.To).Should().Equal("patientbounce@test.com");
            await _factory.SeedAsync(async db =>
                (await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == failingId)).ReminderSentAt.Should().NotBeNull());
        }

        [Fact]
        public async Task Run_WithNothingDue_SendsNothing()
        {
            await _factory.SeedAsync(async db => await AddAppointmentAsync(db, "far", hoursFromNow: 48));

            await _job.ProcessRemindersAsync(CancellationToken.None);

            _factory.Emails.Sent.Should().BeEmpty();
            await _factory.SeedAsync(async db => (await db.Notifications.AnyAsync()).Should().BeFalse());
        }

        // ----------------------------------------------------------------

        // One patient per appointment (email patient{suffix}@test.com), with a slot hoursFromNow from now (clinic time)
        private async Task<int> AddAppointmentAsync(ClinicDbContext db, string suffix, int hoursFromNow,
            AppointmentStatus status = AppointmentStatus.Confirmed, bool alreadyReminded = false)
        {
            var patientUserId = await SeedDataHelper.SeedPatientOnlyAsync(db, suffix);
            var patientId = db.PatientProfiles.Single(p => p.UserId == patientUserId).Id;

            var now = _factory.ClinicNow;
            var start = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Unspecified).AddHours(hoursFromNow);
            var slot = new AppointmentSlot
            {
                BranchId = Branch.MainId,
                DoctorProfileId = _doctorId,
                DoctorScheduleId = _scheduleId,
                Date = DateOnly.FromDateTime(start),
                StartTime = TimeOnly.FromDateTime(start),
                EndTime = TimeOnly.FromDateTime(start.AddMinutes(30)),
                StartsAtUtc = SeedDataHelper.SlotUtc(DateOnly.FromDateTime(start), TimeOnly.FromDateTime(start)),
                EndsAtUtc = SeedDataHelper.SlotUtc(DateOnly.FromDateTime(start), TimeOnly.FromDateTime(start.AddMinutes(30))),
                Status = SlotStatus.Booked
            };
            db.AppointmentSlots.Add(slot);
            await db.SaveChangesAsync();

            var appointment = new Appointment
            {
                BranchId = Branch.MainId,
                PatientProfileId = patientId,
                DoctorProfileId = _doctorId,
                SlotId = slot.Id,
                ReminderSentAt = alreadyReminded ? DateTime.UtcNow.AddHours(-1) : null
            };
            db.Appointments.Add(appointment).Property(a => a.Status).CurrentValue = status;
            await db.SaveChangesAsync();
            return appointment.Id;
        }
    }
}
