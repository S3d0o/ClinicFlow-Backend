using ClinicFlow.BackgroundJobs;
using ClinicFlow.Domain.Enums;
using ClinicFlow.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClinicFlow.IntegrationTests.Tests.BackgroundJobs
{
    // REVIEW.md H8: runs single passes of the daily job against SQL Server
    [Collection(nameof(IntegrationTestCollection))]
    public class SlotGenerationJobTests : IAsyncLifetime
    {
        private int _doctorId;
        private int _scheduleId;
        private int _bookedSlotId;
        private DayOfWeek _day;

        private readonly ClinicFlowApiFactory _factory;
        private readonly SlotGenerationJob _job;

        public SlotGenerationJobTests(ClinicFlowApiFactory factory)
        {
            _factory = factory;
            _job = new SlotGenerationJob(
                factory.Services.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<SlotGenerationJob>.Instance);
        }

        public async Task InitializeAsync()
        {
            await _factory.ResetDataBaseAsync();

            // A 09:00–17:00 schedule (16 half-hour slots a day) that only has one booked slot, three days out
            _day = _factory.ClinicToday.AddDays(3).DayOfWeek;
            await _factory.SeedAsync(async db =>
            {
                (_, _doctorId) = await SeedDataHelper.SeedDoctorAsync(db, "rolling");
                (_scheduleId, var slotIds) = await SeedDataHelper.SeedScheduleWithSlotsAsync(db, _doctorId, _day,
                    (3, new TimeOnly(9, 0), SlotStatus.Booked));
                _bookedSlotId = slotIds[0];
            });
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task Run_FillsTheNext14DaysAndKeepsExistingSlots()
        {
            await _job.GenerateSlotsAsync(CancellationToken.None);

            await _factory.SeedAsync(async db =>
            {
                var slots = await db.AppointmentSlots.AsNoTracking().Where(s => s.DoctorScheduleId == _scheduleId).ToListAsync();
                var today = _factory.ClinicToday;

                // The weekday falls twice in the window; the booked 09:00 slot is kept, not duplicated
                slots.Should().HaveCount(2 * 16);
                slots.Should().OnlyContain(s => s.Date >= today && s.Date <= today.AddDays(13) && s.Date.DayOfWeek == _day);
                slots.Single(s => s.Date == today.AddDays(3) && s.StartTime == new TimeOnly(9, 0)).Id.Should().Be(_bookedSlotId);
            });
        }

        [Fact]
        public async Task Run_Twice_CreatesNoDuplicates()
        {
            await _job.GenerateSlotsAsync(CancellationToken.None);
            await _job.GenerateSlotsAsync(CancellationToken.None);

            await _factory.SeedAsync(async db =>
                (await db.AppointmentSlots.CountAsync(s => s.DoctorScheduleId == _scheduleId)).Should().Be(2 * 16));
        }

        [Fact]
        public async Task Run_SkipsInactiveSchedules()
        {
            await _factory.SeedAsync(async db =>
                await db.DoctorSchedules.Where(s => s.Id == _scheduleId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false)));

            await _job.GenerateSlotsAsync(CancellationToken.None);

            await _factory.SeedAsync(async db =>
                (await db.AppointmentSlots.CountAsync(s => s.DoctorScheduleId == _scheduleId)).Should().Be(1));
        }
    }
}
