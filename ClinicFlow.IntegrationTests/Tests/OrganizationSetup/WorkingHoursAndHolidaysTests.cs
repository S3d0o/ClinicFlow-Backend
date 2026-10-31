using ClinicFlow.BackgroundJobs;
using ClinicFlow.Domain.Enums;
using ClinicFlow.IntegrationTests.Infrastructure;
using Domain.Entities.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Authorization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace ClinicFlow.IntegrationTests.Tests.OrganizationSetup
{
    // A branch's working hours and holidays over HTTP (P1 PR 2; API_CONTRACT.md §13.25): the week's shape and rules,
    // the scopes, the affected visits and withdrawn slots of a new holiday, and what slot generation and the inherited
    // schedule route do with them
    [Collection(nameof(IntegrationTestCollection))]
    public class WorkingHoursAndHolidaysTests(ClinicFlowApiFactory factory) : IAsyncLifetime
    {
        private static readonly string HoursUrl = $"/api/v2/branches/{Branch.MainId}/working-hours";
        private static readonly string HolidaysUrl = $"/api/v2/branches/{Branch.MainId}/holidays";
        private const string AddHolidayUrl = "/api/v2/holidays";

        private readonly HttpClient _client = factory.CreateClient();
        private Guid _doctorUserId;
        private int _bookedSlotId;
        private int _freeDoctorId;
        private int _freeScheduleId;

        // The booked visit is three days out at 10:00; the second doctor has free slots that day and the next
        private DateOnly VisitDay => factory.ClinicToday.AddDays(3);

        public async Task InitializeAsync()
        {
            await factory.ResetDataBaseAsync();
            _client.DefaultRequestHeaders.Clear();

            await factory.SeedAsync(async db =>
            {
                (_, _doctorUserId, _bookedSlotId, _) = await SeedDataHelper.SeedBaseAppointmentAsync(db);
                // The helper leaves the slot Available; a booking marks it Booked
                (await db.AppointmentSlots.SingleAsync(s => s.Id == _bookedSlotId)).Status = SlotStatus.Booked;
                await db.SaveChangesAsync();
                (_, _freeDoctorId) = await SeedDataHelper.SeedDoctorAsync(db, "free");
                (_freeScheduleId, _) = await SeedDataHelper.SeedScheduleWithSlotsAsync(db, _freeDoctorId, VisitDay.DayOfWeek,
                    (3, new TimeOnly(9, 0), SlotStatus.Available),
                    (3, new TimeOnly(9, 30), SlotStatus.Available),
                    (4, new TimeOnly(9, 0), SlotStatus.Available));
            });
        }

        public Task DisposeAsync() => Task.CompletedTask;

        private void AuthenticateAs(string role, Guid? userId = null)
        {
            _client.DefaultRequestHeaders.Clear();
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, (userId ?? Guid.NewGuid()).ToString());
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserRoleHeader, role);
        }

        private static object Week(params (int Weekday, string[] Periods)[] days)
            => days.Select(d => new
            {
                weekday = d.Weekday,
                ranges = d.Periods.Select(p => new { from = p[..5], to = p[6..] }).ToArray()
            }).ToArray();

        private object AHoliday(string? branchId = "1", int fromDays = 3, int toDays = 3, bool recursYearly = false) => new
        {
            branchId,
            nameEn = "Maintenance",
            nameAr = "صيانة",
            from = factory.ClinicToday.AddDays(fromDays).ToString("yyyy-MM-dd"),
            to = factory.ClinicToday.AddDays(toDays).ToString("yyyy-MM-dd"),
            recursYearly,
        };

        private static async Task<string?> TitleOf(HttpResponseMessage response)
            => (await response.Content.ReadFromJsonAsync<JsonObject>())!["title"]?.GetValue<string>();

        private async Task RunSlotGenerationAsync()
            => await new SlotGenerationJob(factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<SlotGenerationJob>.Instance)
                .GenerateSlotsAsync(CancellationToken.None);

        // ── Working hours ───────────────────────────────────────────────────

        [Fact]
        public async Task GetWorkingHours_AnExistingBranch_IsOpenAroundTheClockUntilSet()
        {
            AuthenticateAs(Roles.Doctor);

            var response = await _client.GetAsync(HoursUrl);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var week = (await response.Content.ReadFromJsonAsync<JsonArray>())!;
            week.Select(d => d!["weekday"]!.GetValue<int>()).Should().Equal(0, 1, 2, 3, 4, 5, 6);
            week.Should().OnlyContain(d => d!["ranges"]!.ToJsonString() == """[{"from":"00:00","to":"23:59"}]""");
        }

        [Fact]
        public async Task SetWorkingHours_Admin_Returns200AndTheWeekIsReadBack()
        {
            AuthenticateAs(Roles.Admin);

            var response = await _client.PutAsJsonAsync(HoursUrl, Week((6, ["16:00-22:00", "09:00-15:00"]), (5, ["13:00-21:00"])));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var week = (await (await _client.GetAsync(HoursUrl)).Content.ReadFromJsonAsync<JsonArray>())!;
            week[0]!["ranges"]!.AsArray().Should().BeEmpty(); // Sunday wasn't sent: closed
            week[5]!["ranges"]!.ToJsonString().Should().Be("""[{"from":"13:00","to":"21:00"}]""");
            week[6]!["ranges"]!.ToJsonString().Should().Be("""[{"from":"09:00","to":"15:00"},{"from":"16:00","to":"22:00"}]""");

            bool audited = false;
            await factory.SeedAsync(async db => audited = await db.AuditEntries.AnyAsync(e => e.EntityType == "BranchWorkingHours"));
            audited.Should().BeTrue();
        }

        [Fact]
        public async Task SetWorkingHours_OverlappingPeriods_Returns400InvalidHoursAsShapeA()
        {
            AuthenticateAs(Roles.Admin);

            var response = await _client.PutAsJsonAsync(HoursUrl, Week((1, ["09:00-15:00", "14:00-22:00"])));

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await TitleOf(response)).Should().Be("Branch.InvalidHours");
        }

        [Fact]
        public async Task SetWorkingHours_Doctor_Returns403()
        {
            AuthenticateAs(Roles.Doctor);

            var response = await _client.PutAsJsonAsync(HoursUrl, Week());

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        [Fact]
        public async Task SetWorkingHours_UnknownBranch_Returns404()
        {
            AuthenticateAs(Roles.Admin);

            var response = await _client.PutAsJsonAsync("/api/v2/branches/999/working-hours", Week());

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await TitleOf(response)).Should().Be("Branch.NotFound");
        }

        [Fact]
        public async Task SlotGeneration_AfterNewHours_OnlyMakesSlotsInsideThem()
        {
            // The free doctor's 09:00–17:00 schedule; the branch now opens at 12:00 every day
            AuthenticateAs(Roles.Admin);
            var everyDay = Enumerable.Range(0, 7).Select(d => (d, new[] { "12:00-22:00" })).ToArray();
            (await _client.PutAsJsonAsync(HoursUrl, Week(everyDay))).StatusCode.Should().Be(HttpStatusCode.OK);

            await RunSlotGenerationAsync();

            await factory.SeedAsync(async db =>
            {
                var generated = await db.AppointmentSlots.AsNoTracking()
                    .Where(s => s.DoctorScheduleId == _freeScheduleId && s.Date > VisitDay.AddDays(1))
                    .ToListAsync();
                generated.Should().NotBeEmpty();
                generated.Should().OnlyContain(s => s.StartTime >= new TimeOnly(12, 0) && s.EndTime <= new TimeOnly(17, 0));
            });
        }

        [Fact]
        public async Task CreateSchedule_OutsideTheBranchHours_Returns400ScheduleOutsideBranchHours()
        {
            AuthenticateAs(Roles.Admin);
            var everyDay = Enumerable.Range(0, 7).Select(d => (d, new[] { "12:00-22:00" })).ToArray();
            await _client.PutAsJsonAsync(HoursUrl, Week(everyDay));
            AuthenticateAs(Roles.Doctor, _doctorUserId);

            var response = await _client.PostAsJsonAsync("/api/doctors/schedule", new
            {
                dayOfWeek = factory.ClinicToday.AddDays(1).DayOfWeek.ToString(),
                startTime = "09:00:00",
                endTime = "13:00:00",
                slotDurationMinutes = 30,
            });

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await response.Content.ReadAsStringAsync()).Should().Contain("Doctor.ScheduleOutsideBranchHours");
        }

        // ── Holidays ────────────────────────────────────────────────────────

        [Fact]
        public async Task AddHoliday_ForTheBranch_Returns201WithTheAffectedVisitAndWithdrawsItsFreeSlots()
        {
            AuthenticateAs(Roles.Admin);

            var response = await _client.PostAsJsonAsync(AddHolidayUrl, AHoliday());

            response.StatusCode.Should().Be(HttpStatusCode.Created);
            var body = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
            body["affectedAppointments"]!.GetValue<int>().Should().Be(1);
            body["holiday"]!["branchId"]!.GetValue<string>().Should().Be("1");
            body["holiday"]!["id"]!.GetValue<string>().Should().NotBeNullOrEmpty();

            await factory.SeedAsync(async db =>
            {
                var slots = await db.AppointmentSlots.AsNoTracking().Where(s => s.Date == VisitDay).ToListAsync();
                slots.Should().ContainSingle(s => s.Id == _bookedSlotId && s.Status == SlotStatus.Booked); // the visit stays
                slots.Should().HaveCount(1);
                (await db.AppointmentSlots.CountAsync(s => s.Date == VisitDay.AddDays(1))).Should().Be(1); // the next day keeps its own
                (await db.AuditEntries.AnyAsync(e => e.EntityType == "Holiday")).Should().BeTrue();
            });
        }

        [Fact]
        public async Task AddHoliday_ForEveryBranch_AdminReturns201_ManagerReturns403OutOfScope()
        {
            AuthenticateAs(Roles.Manager);
            var refused = await _client.PostAsJsonAsync(AddHolidayUrl, AHoliday(branchId: null));
            refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await TitleOf(refused)).Should().Be("Access.OutOfScope");

            AuthenticateAs(Roles.Admin);
            var added = await _client.PostAsJsonAsync(AddHolidayUrl, AHoliday(branchId: null));
            added.StatusCode.Should().Be(HttpStatusCode.Created);
            (await added.Content.ReadFromJsonAsync<JsonObject>())!["holiday"]!["branchId"].Should().BeNull();
        }

        [Fact]
        public async Task AddHoliday_ForTheBranch_ManagerReturns201()
        {
            AuthenticateAs(Roles.Manager);

            var response = await _client.PostAsJsonAsync(AddHolidayUrl, AHoliday());

            response.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        [Fact]
        public async Task AddHoliday_EndBeforeStart_Returns400WithTheToKey()
        {
            AuthenticateAs(Roles.Admin);

            var response = await _client.PostAsJsonAsync(AddHolidayUrl, AHoliday(fromDays: 5, toDays: 4));

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await response.Content.ReadFromJsonAsync<JsonObject>())!["errors"]!.AsObject().ContainsKey("to").Should().BeTrue();
        }

        [Fact]
        public async Task AddHoliday_UnknownBranch_Returns404()
        {
            AuthenticateAs(Roles.Admin);

            var response = await _client.PostAsJsonAsync(AddHolidayUrl, AHoliday(branchId: "999"));

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await TitleOf(response)).Should().Be("Branch.NotFound");
        }

        [Fact]
        public async Task ListHolidays_ReturnsTheBranchsAndEveryBranchHolidays_ByStartDate_WithoutPastOnes()
        {
            await factory.SeedAsync(async db =>
            {
                var other = db.Branches.Add(new Branch { Code = "NSR", NameEn = "Nasr City", NameAr = "مدينة نصر", TimeZoneId = "Africa/Cairo" }).Entity;
                await db.SaveChangesAsync();
                var today = factory.ClinicToday;
                db.Holidays.AddRange(
                    new BranchHoliday { BranchId = Branch.MainId, NameEn = "Main later", NameAr = "ب", From = today.AddDays(20), To = today.AddDays(20) },
                    new BranchHoliday { BranchId = null, NameEn = "Everywhere", NameAr = "أ", From = today.AddDays(10), To = today.AddDays(11) },
                    new BranchHoliday { BranchId = other.Id, NameEn = "Other branch", NameAr = "ج", From = today.AddDays(5), To = today.AddDays(5) },
                    new BranchHoliday { BranchId = Branch.MainId, NameEn = "Last year", NameAr = "د", From = new DateOnly(today.Year - 1, 3, 1), To = new DateOnly(today.Year - 1, 3, 2) },
                    new BranchHoliday { BranchId = null, NameEn = "Yearly", NameAr = "ه", From = new DateOnly(2020, 1, 7), To = new DateOnly(2020, 1, 7), RecursYearly = true });
                await db.SaveChangesAsync();
            });
            AuthenticateAs(Roles.Receptionist);

            var thisYearOn = (await (await _client.GetAsync(HolidaysUrl)).Content.ReadFromJsonAsync<JsonArray>())!;
            var lastYear = (await (await _client.GetAsync($"{HolidaysUrl}?year={factory.ClinicToday.Year - 1}")).Content.ReadFromJsonAsync<JsonArray>())!;

            thisYearOn.Select(h => h!["nameEn"]!.GetValue<string>()).Should().Equal("Yearly", "Everywhere", "Main later");
            lastYear.Select(h => h!["nameEn"]!.GetValue<string>()).Should().Equal("Yearly", "Last year");
        }

        [Fact]
        public async Task RemoveHoliday_Returns204_ThenGenerationFillsTheDayAgain()
        {
            AuthenticateAs(Roles.Admin);
            var added = await _client.PostAsJsonAsync(AddHolidayUrl, AHoliday());
            var id = (await added.Content.ReadFromJsonAsync<JsonObject>())!["holiday"]!["id"]!.GetValue<string>();

            var response = await _client.DeleteAsync($"{AddHolidayUrl}/{id}");
            await RunSlotGenerationAsync();

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await _client.DeleteAsync($"{AddHolidayUrl}/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
            await factory.SeedAsync(async db =>
                (await db.AppointmentSlots.CountAsync(s => s.DoctorScheduleId == _freeScheduleId && s.Date == VisitDay)).Should().Be(16));
        }
    }
}
