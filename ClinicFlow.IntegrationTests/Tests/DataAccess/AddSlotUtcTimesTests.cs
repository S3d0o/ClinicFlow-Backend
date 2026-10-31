using ClinicFlow.IntegrationTests.Infrastructure;
using Domain.Entities.AppModule;
using Domain.Entities.IdentityModule;
using Domain.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Persistence.AppData;

namespace ClinicFlow.IntegrationTests.Tests.DataAccess
{
    // AddSlotUtcTimes fills StartsAtUtc/EndsAtUtc of existing slots with SQL Server's AT TIME ZONE, which uses Windows
    // zone names and its own zone data, while new slots get them from .NET (WallClock). Both must give the same instants,
    // including around Cairo's daylight saving changes in 2026 (clocks forward 24 Apr 00:00, back 29 Oct 24:00)
    [Collection(nameof(IntegrationTestCollection))]
    public class AddSlotUtcTimesTests(ClinicFlowApiFactory factory) : IAsyncLifetime
    {
        private const string PreviousMigration = "20261023163542_AddOrganizationAndBranches";
        private static readonly TimeZoneInfo Cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");

        // (date, start, end, why)
        private static readonly (string Date, string Start, string End, string Case)[] Slots =
        [
            ("2026-01-15", "10:00", "10:30", "winter, UTC+2"),
            ("2026-07-15", "10:00", "10:30", "summer, UTC+3"),
            ("2026-04-23", "23:00", "23:30", "the evening before the clocks go forward"),
            ("2026-04-24", "01:30", "02:00", "just after the skipped hour"),
            ("2026-10-29", "22:00", "22:30", "the last hour before the clocks go back"),
            ("2026-10-29", "23:00", "23:30", "the repeated hour: its first occurrence"),
            ("2026-10-30", "09:00", "09:30", "the morning after, UTC+2"),
        ];

        public async Task InitializeAsync()
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ClinicDbContext>();
            await db.Database.EnsureDeletedAsync();
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task Migrate_ExistingSlots_GetTheSameUtcTimesAsDotNetComputes()
        {
            // Users, specialties and schedules have the same columns then as in today's model, so EF can seed them;
            // doctor profiles (StaffMemberId since P1 PR 4) and slots don't, so they are inserted with SQL
            var doctorId = 0;
            var scheduleId = 0;
            await factory.SeedAsync(async db =>
            {
                var specialty = db.Specialties.Add(new Specialty { Name = "General Practice", IsActive = true }).Entity;
                var user = db.Users.Add(new ApplicationUser
                {
                    Id = Guid.NewGuid(), UserName = "doctortz@test.com", Email = "doctortz@test.com", FirstName = "Doctor", LastName = "tz",
                    SecurityStamp = Guid.NewGuid().ToString(), IsActive = true,
                }).Entity;
                await db.SaveChangesAsync();
                doctorId = (await db.Database.SqlQuery<int>($"""
                    INSERT INTO Doctors (OrganizationId, UserId, SpecialtyId, YearsOfExperience, ConsultationFee, AverageRating, TotalReviews, IsApprovedByAdmin)
                    OUTPUT INSERTED.Id AS Value
                    VALUES (1, {user.Id}, {specialty.Id}, 5, 200, 0, 0, 1)
                    """).ToListAsync()).Single();
                (scheduleId, _) = await SeedDataHelper.SeedScheduleWithSlotsAsync(db, doctorId, DayOfWeek.Monday);
            });

            await factory.SeedAsync(async db =>
            {
                foreach (var (date, start, end, _) in Slots)
                    await db.Database.ExecuteSqlAsync($"""
                        INSERT INTO AppointmentSlots (OrganizationId, BranchId, DoctorProfileId, DoctorScheduleId, [Date], StartTime, EndTime, Status)
                        VALUES (1, 1, {doctorId}, {scheduleId}, {DateOnly.Parse(date)}, {TimeOnly.Parse(start)}, {TimeOnly.Parse(end)}, 'Available')
                        """);
            });

            await factory.SeedAsync(db => db.GetService<IMigrator>().MigrateAsync());

            await factory.SeedAsync(async db =>
            {
                var migrated = await db.AppointmentSlots.AsNoTracking().ToListAsync();
                foreach (var (date, start, end, why) in Slots)
                {
                    var slot = migrated.Single(s => s.Date == DateOnly.Parse(date) && s.StartTime == TimeOnly.Parse(start));
                    slot.StartsAtUtc.Should().Be(WallClock.ToUtc(Cairo, slot.Date, slot.StartTime), $"StartsAtUtc for {why}");
                    slot.EndsAtUtc.Should().Be(WallClock.ToUtc(Cairo, slot.Date, TimeOnly.Parse(end)), $"EndsAtUtc for {why}");
                }
            });
        }
    }
}
