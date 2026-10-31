using ClinicFlow.Domain.Enums;
using ClinicFlow.IntegrationTests.Infrastructure;
using Domain.Entities.AppModule;
using Domain.Entities.Auditing;
using Domain.Entities.Tenancy;
using Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Persistence.AppData;
using Persistence.Transactions;
using Shared.DTOs.Appointment;
using Shared.DTOs.Auth;
using Shared.Enums;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace ClinicFlow.IntegrationTests.Tests.Audit
{
    // The audit trail (FR-AUD-01, FR-AUD-04, P0 PR 10): changes to appointments, doctor approval and suspension, and
    // roles write entries in the same transaction as the change; entries are never changed or deleted
    [Collection(nameof(IntegrationTestCollection))]
    public class AuditTests(ClinicFlowApiFactory factory) : IAsyncLifetime
    {
        private readonly HttpClient _client = factory.CreateClient();
        private Guid _patientUserId, _doctorUserId;
        private int _slotId, _appointmentId;

        public async Task InitializeAsync()
        {
            await factory.ResetDataBaseAsync();
            _client.DefaultRequestHeaders.Clear();
            await factory.SeedAsync(async db =>
                (_patientUserId, _doctorUserId, _slotId, _appointmentId) = await SeedDataHelper.SeedBaseAppointmentAsync(db));
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task SeededData_WritesNoEntry()
            => (await EntriesAsync()).Should().BeEmpty();

        [Fact]
        public async Task BookAppointment_WritesACreateEntryByThePatient_WithTheReasonRedacted()
        {
            As(_patientUserId, "Patient");

            var response = await _client.PostAsJsonAsync("/api/appointments", new BookAppointmentRequest(_slotId, "Chest pain"));

            response.StatusCode.Should().Be(HttpStatusCode.Created);
            var appointmentId = (await response.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<int>();
            var entry = (await EntriesAsync()).Should().ContainSingle().Subject;
            entry.Action.Should().Be(AuditAction.Create);
            entry.EntityType.Should().Be("Appointment");
            entry.EntityId.Should().Be(appointmentId.ToString()); // the database's key, written after the insert
            entry.UserId.Should().Be(_patientUserId);
            entry.OrganizationId.Should().Be(Organization.DefaultId);
            entry.CorrelationId.Should().NotBeNullOrEmpty();
            entry.OccurredAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));

            var changes = Changes(entry);
            changes["Status"]!["new"]!.GetValue<string>().Should().Be("Confirmed");
            changes["SlotId"]!["new"]!.GetValue<int>().Should().Be(_slotId);
            changes["ReasonForVisit"]!.ToJsonString().Should().Be("""{"changed":true}""");
            entry.Changes.Should().NotContain("Chest pain");
        }

        [Fact]
        public async Task CancelByAdmin_WritesAnUpdateEntry_WithOnlyTheChangedValues()
        {
            var adminId = Guid.NewGuid();
            As(adminId, "Admin");

            var response = await _client.PostAsJsonAsync($"/api/appointments/{_appointmentId}/cancel", new CancelAppointmentRequest("Clinic closed"));

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            var entry = (await EntriesAsync()).Should().ContainSingle().Subject;
            entry.Action.Should().Be(AuditAction.Update);
            entry.EntityId.Should().Be(_appointmentId.ToString());
            entry.UserId.Should().Be(adminId);

            var changes = Changes(entry);
            changes.Select(c => c.Key).Should().BeEquivalentTo("Status", "CancelledAt", "CancelledBy", "CancellationReason");
            changes["Status"]!.ToJsonString().Should().Be("""{"old":"Confirmed","new":"Cancelled"}""");
            changes["CancelledBy"]!.ToJsonString().Should().Be("""{"old":null,"new":"Admin"}""");
            changes["CancellationReason"]!["new"]!.GetValue<string>().Should().Be("Clinic closed");
        }

        [Fact]
        public async Task AddDoctorNotes_RecordsOnlyThatTheNotesChanged()
        {
            As(_doctorUserId, "Doctor");

            var response = await _client.PutAsJsonAsync($"/api/appointments/{_appointmentId}/notes", new DoctorNotesRequest("Hypertension; follow up in 2 weeks"));

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            var entry = (await EntriesAsync()).Should().ContainSingle().Subject;
            entry.UserId.Should().Be(_doctorUserId);
            entry.Changes.Should().Be("""{"DoctorNotes":{"changed":true}}""");
        }

        [Fact]
        public async Task ApproveDoctor_WritesAnEntryByTheAdmin()
        {
            var doctorProfileId = 0;
            await factory.SeedAsync(async db => (_, doctorProfileId) = await SeedDataHelper.SeedDoctorAsync(db, "pending", approved: false));
            var adminId = Guid.NewGuid();
            As(adminId, "Admin");

            var response = await _client.PostAsync($"/api/admin/doctors/{doctorProfileId}/approve", null);

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            var entry = (await EntriesAsync()).Should().ContainSingle().Subject;
            entry.EntityType.Should().Be("DoctorProfile");
            entry.EntityId.Should().Be(doctorProfileId.ToString());
            entry.UserId.Should().Be(adminId);
            entry.Changes.Should().Be("""{"IsApprovedByAdmin":{"old":false,"new":true}}""");
        }

        [Fact]
        public async Task SuspendDoctor_WritesAUserEntry()
        {
            var doctorProfileId = 0;
            await factory.SeedAsync(async db => doctorProfileId = (await db.DoctorProfiles.SingleAsync(d => d.UserId == _doctorUserId)).Id);
            As(Guid.NewGuid(), "Admin");

            var response = await _client.PostAsync($"/api/admin/doctors/{doctorProfileId}/suspend", null);

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            var entry = (await EntriesAsync()).Should().ContainSingle().Subject;
            entry.EntityType.Should().Be("User");
            entry.EntityId.Should().Be(_doctorUserId.ToString());
            entry.Changes.Should().Be("""{"IsActive":{"old":true,"new":false}}"""); // none of Identity's bookkeeping
        }

        [Fact]
        public async Task RegisterDoctor_AuditsTheAccountTheRoleAndTheProfile_AsOneRequest()
        {
            var specialtyId = 0;
            await factory.SeedAsync(async db =>
            {
                await SeedDataHelper.SeedRolesAsync(db);
                specialtyId = (await db.Specialties.FirstAsync()).Id;
            });

            var response = await _client.PostAsJsonAsync("/api/auth/register/doctor", new RegisterDoctorRequest(
                "Omar", "Fathy", "new.doctor@test.com", "Register@1234", "+201009876543", Gender.Male, null,
                specialtyId, "Cardiologist", 12, 450m, "12 Tahrir St", "Cairo"));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            Guid userId = default;
            var profileId = 0;
            await factory.SeedAsync(async db =>
            {
                userId = (await db.Users.SingleAsync(u => u.Email == "new.doctor@test.com")).Id;
                profileId = (await db.DoctorProfiles.SingleAsync(d => d.UserId == userId)).Id;
            });

            var entries = await EntriesAsync();
            entries.Select(e => (e.Action, e.EntityType)).Should().BeEquivalentTo(
                [(AuditAction.Create, "User"), (AuditAction.Create, "UserRole"), (AuditAction.Create, "DoctorProfile")]);
            entries.Should().OnlyContain(e => e.UserId == null);            // nobody is signed in yet
            entries.Select(e => e.CorrelationId).Distinct().Should().ContainSingle();

            Changes(entries.Single(e => e.EntityType == "User"))["IsActive"]!["new"]!.GetValue<bool>().Should().BeTrue();
            var role = entries.Single(e => e.EntityType == "UserRole");
            role.EntityId.Should().StartWith(userId.ToString());
            Changes(role)["Role"]!.ToJsonString().Should().Be("""{"new":"Doctor"}""");
            var profile = entries.Single(e => e.EntityType == "DoctorProfile");
            profile.EntityId.Should().Be(profileId.ToString());
            Changes(profile)["IsApprovedByAdmin"]!["new"]!.GetValue<bool>().Should().BeFalse();
        }

        [Fact]
        public async Task AFailedSave_WritesNoEntry()
        {
            // The entry and the change share one save: if it fails, neither is written
            var act = () => factory.SeedAsync(async db =>
            {
                db.RecordAudit = true;
                var appointment = await db.Appointments.Include(a => a.Slot).SingleAsync(a => a.Id == _appointmentId);
                appointment.Cancel(CancelledBy.Admin, DateTime.UtcNow, null, TimeSpan.FromHours(2));
                db.Entry(appointment).Property(a => a.SlotId).CurrentValue = int.MaxValue; // unknown slot: FK violation
                await db.SaveChangesAsync();
            });

            await act.Should().ThrowAsync<DbUpdateException>();
            (await EntriesAsync()).Should().BeEmpty();
        }

        [Fact]
        public async Task ARolledBackInsert_WritesNoEntry()
        {
            // An inserted row's entry is written by a second save, inside the same transaction
            await factory.SeedAsync(db => db.ExecuteInTransactionAsync(async _ =>
            {
                db.RecordAudit = true;
                db.Appointments.Add(await AnotherAppointmentAsync(db));
                await db.SaveChangesAsync();
                (await db.AuditEntries.CountAsync()).Should().Be(1); // written, but never committed
            }, CancellationToken.None));

            (await EntriesAsync()).Should().BeEmpty();
            await factory.SeedAsync(async db => (await db.Appointments.CountAsync()).Should().Be(1));
        }

        [Fact]
        public async Task AnAuditedInsertOutsideATransaction_Throws()
        {
            // Its entry couldn't commit with it, so the insert isn't attempted
            var act = () => factory.SeedAsync(async db =>
            {
                db.RecordAudit = true;
                db.Appointments.Add(await AnotherAppointmentAsync(db));
                await db.SaveChangesAsync();
            });

            (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*ExecuteInTransactionAsync*");
            await factory.SeedAsync(async db => (await db.Appointments.CountAsync()).Should().Be(1));
        }

        [Fact]
        public async Task AChangeToIgnoredPropertiesOnly_WritesNoEntry()
        {
            await factory.SeedAsync(async db =>
            {
                db.RecordAudit = true;
                (await db.Appointments.SingleAsync(a => a.Id == _appointmentId)).QueueReminder(DateTime.UtcNow);
                await db.SaveChangesAsync();
            });

            (await EntriesAsync()).Should().BeEmpty();
        }

        [Fact]
        public async Task AuditEntries_CannotBeChangedOrDeleted()
        {
            As(Guid.NewGuid(), "Admin");
            (await _client.PostAsJsonAsync($"/api/appointments/{_appointmentId}/cancel", new CancelAppointmentRequest(null)))
                .StatusCode.Should().Be(HttpStatusCode.NoContent);

            var change = () => factory.SeedAsync(async db =>
            {
                var entry = await db.AuditEntries.SingleAsync();
                db.Entry(entry).Property(e => e.Reason).CurrentValue = "rewritten";
                await db.SaveChangesAsync();
            });
            var delete = () => factory.SeedAsync(async db =>
            {
                db.AuditEntries.Remove(await db.AuditEntries.SingleAsync());
                await db.SaveChangesAsync();
            });

            (await change.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*append-only*");
            (await delete.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*append-only*");
            (await EntriesAsync()).Should().ContainSingle().Which.Reason.Should().BeNull();
        }

        private void As(Guid userId, string role)
        {
            _client.DefaultRequestHeaders.Clear();
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId.ToString());
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserRoleHeader, role);
        }

        // A second appointment for the seeded patient, doctor and slot
        private async Task<Appointment> AnotherAppointmentAsync(ClinicDbContext db)
        {
            var seeded = await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == _appointmentId);
            return new Appointment
            {
                BranchId = Branch.MainId,
                SlotId = _slotId,
                PatientProfileId = seeded.PatientProfileId,
                DoctorProfileId = seeded.DoctorProfileId,
            };
        }

        private async Task<List<AuditEntry>> EntriesAsync()
        {
            List<AuditEntry> entries = [];
            await factory.SeedAsync(async db => entries = await db.AuditEntries.AsNoTracking().OrderBy(e => e.Id).ToListAsync());
            return entries;
        }

        private static JsonObject Changes(AuditEntry entry) => JsonNode.Parse(entry.Changes)!.AsObject();
    }
}
