using ClinicFlow.BackgroundJobs;
using ClinicFlow.Domain.Enums;
using ClinicFlow.IntegrationTests.Infrastructure;
using Domain.Entities.IdentityModule;
using Domain.Entities.Messaging;
using Domain.Enums;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Persistence.AppData;
using Persistence.Outbox;
using Shared.DTOs.Appointment;
using Shared.DTOs.Auth;
using System.Net;
using System.Net.Http.Json;

namespace ClinicFlow.IntegrationTests.Tests.Messaging
{
    // The transactional outbox (docs/v2/06 §5): events are written with the change that raised them, and delivered
    // later, at least once, with retries, backoff and a dead letter
    [Collection(nameof(IntegrationTestCollection))]
    public class OutboxTests(ClinicFlowApiFactory factory) : IAsyncLifetime
    {
        private readonly HttpClient _client = factory.CreateClient();
        private Guid _patientUserId, _doctorUserId;
        private int _appointmentId;

        public async Task InitializeAsync()
        {
            await factory.ResetDataBaseAsync();
            _client.DefaultRequestHeaders.Clear();
            await factory.SeedAsync(async db =>
                (_patientUserId, _doctorUserId, _, _appointmentId) = await SeedDataHelper.SeedBaseAppointmentAsync(db));
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task Cancel_WritesTheEvent_AndNotifiesOnlyWhenDelivered()
        {
            await CancelAsAdminAsync();

            var message = (await MessagesAsync()).Should().ContainSingle().Subject;
            message.Type.Should().Be("Domain.Events.AppointmentCancelled");
            message.ProcessedAtUtc.Should().BeNull();
            (await NotificationsAsync()).Should().BeEmpty(); // nothing is sent inside the request

            (await factory.DrainOutboxAsync()).Should().Be(1);

            (await NotificationsAsync()).Select(n => n.UserId).Should().BeEquivalentTo([_patientUserId, _doctorUserId]);
            message = (await MessagesAsync()).Single();
            message.ProcessedAtUtc.Should().NotBeNull();
            message.Attempts.Should().Be(1);
            message.LockedUntilUtc.Should().BeNull();
        }

        [Fact]
        public async Task AFailedSave_WritesNoEvent()
        {
            // The event and the change share one transaction: if the save fails, neither is written
            var act = () => factory.SeedAsync(async db =>
            {
                var appointment = await db.Appointments.Include(a => a.Slot).SingleAsync(a => a.Id == _appointmentId);
                appointment.Cancel(CancelledBy.Admin, DateTime.UtcNow, null);
                db.Notifications.Add(new Notification { UserId = Guid.NewGuid(), Title = "x", Message = "x", CreatedAt = DateTime.UtcNow }); // unknown user: FK violation
                await db.SaveChangesAsync();
            });

            await act.Should().ThrowAsync<DbUpdateException>();
            (await MessagesAsync()).Should().BeEmpty();
            await factory.SeedAsync(async db =>
                (await db.Appointments.SingleAsync(a => a.Id == _appointmentId)).Status.Should().Be(AppointmentStatus.Confirmed));
        }

        [Fact]
        public async Task ARedeliveredMessage_DoesNotNotifyTwice()
        {
            // At least once: a dispatcher that dies after the handlers but before marking the message delivers it again
            await CancelAsAdminAsync();
            await factory.DrainOutboxAsync();
            await factory.SeedAsync(db => db.OutboxMessages.ExecuteUpdateAsync(s => s.SetProperty(m => m.ProcessedAtUtc, (DateTime?)null)));

            (await factory.DrainOutboxAsync()).Should().Be(1);

            (await NotificationsAsync()).Should().HaveCount(2);
            await factory.SeedAsync(async db => (await db.MessageDeliveries.CountAsync()).Should().Be(1));
        }

        [Fact]
        public async Task AFailingEmail_IsRetriedAfterTheBackoff_AndSentOnce()
        {
            factory.Emails.FailSends = true;
            await ForgotPasswordAsync();
            var t0 = DateTime.UtcNow;

            await factory.DrainOutboxAsync(t0);

            var failed = (await MessagesAsync()).Single();
            failed.ProcessedAtUtc.Should().BeNull();
            failed.Attempts.Should().Be(1);
            failed.LastError.Should().Contain("SMTP unavailable");
            failed.NextAttemptAtUtc.Should().Be(t0 + OutboxProcessor.Backoff(1));
            (await factory.DrainOutboxAsync(t0.AddSeconds(1))).Should().Be(0); // still backing off

            factory.Emails.FailSends = false;
            (await factory.DrainOutboxAsync(t0.AddMinutes(1))).Should().Be(1);

            factory.Emails.Sent.Should().ContainSingle(e => e.Kind == SentEmailKind.PasswordReset);
            (await MessagesAsync()).Single().ProcessedAtUtc.Should().NotBeNull();
        }

        [Fact]
        public async Task AMessageThatKeepsFailing_IsDeadLettered()
        {
            factory.Emails.FailSends = true;
            await ForgotPasswordAsync();

            var at = DateTime.UtcNow;
            for (var attempt = 0; attempt < OutboxProcessor.MaxAttempts; attempt++, at = at.AddHours(2))
                await factory.DrainOutboxAsync(at);

            var message = (await MessagesAsync()).Single();
            message.Attempts.Should().Be(OutboxProcessor.MaxAttempts);
            message.DeadLetteredAtUtc.Should().NotBeNull();
            message.LastError.Should().NotBeNullOrEmpty();
            (await factory.DrainOutboxAsync(at.AddDays(1))).Should().Be(0); // never tried again
        }

        [Fact]
        public async Task AClaimedMessage_IsSkippedUntilItsClaimExpires()
        {
            // Another dispatcher holds it; a dispatcher that died lets go when the claim expires
            await CancelAsAdminAsync();
            var lockedUntil = DateTime.UtcNow.AddMinutes(5);
            await factory.SeedAsync(db => db.OutboxMessages.ExecuteUpdateAsync(s => s.SetProperty(m => m.LockedUntilUtc, lockedUntil)));

            (await factory.DrainOutboxAsync()).Should().Be(0);
            (await factory.DrainOutboxAsync(lockedUntil.AddSeconds(1))).Should().Be(1);
        }

        [Fact]
        public async Task WithRecordingOff_EntityEventsAreNotWritten()
        {
            // The demo seeder's switch: seeded history must not notify anyone
            await factory.SeedAsync(async db =>
            {
                db.RecordDomainEvents = false;
                var appointment = await db.Appointments.Include(a => a.Slot).SingleAsync(a => a.Id == _appointmentId);
                appointment.Cancel(CancelledBy.Admin, DateTime.UtcNow, null);
                await db.SaveChangesAsync();
            });

            (await MessagesAsync()).Should().BeEmpty();
        }

        // ── The OutboxDispatcher job (removed from the test host, so run here by hand) ──

        [Fact]
        public async Task Dispatcher_DeliversWhatIsDue_AndReleasesItsLock()
        {
            await CancelAsAdminAsync();

            await NewDispatcher().DispatchAsync(CancellationToken.None);

            (await NotificationsAsync()).Should().HaveCount(2);
            await using var other = await OpenConnectionAsync();
            (await GetLockAsync(other)).Should().BeGreaterThanOrEqualTo(0); // free again after the pass
            await ReleaseLockAsync(other);
        }

        [Fact]
        public async Task Dispatcher_SkipsThePass_WhileAnotherInstanceHoldsTheLock()
        {
            await CancelAsAdminAsync();
            await using var other = await OpenConnectionAsync();
            (await GetLockAsync(other)).Should().BeGreaterThanOrEqualTo(0);

            await NewDispatcher().DispatchAsync(CancellationToken.None);
            (await NotificationsAsync()).Should().BeEmpty();

            await ReleaseLockAsync(other);
            await NewDispatcher().DispatchAsync(CancellationToken.None);
            (await NotificationsAsync()).Should().HaveCount(2);
        }

        private OutboxDispatcher NewDispatcher() => new(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            factory.Services.GetRequiredService<OutboxProcessor>(),
            NullLogger<OutboxDispatcher>.Instance);

        private async Task<SqlConnection> OpenConnectionAsync()
        {
            var connection = new SqlConnection(factory.TestConnectionString);
            await connection.OpenAsync();
            return connection;
        }

        private static async Task<int> GetLockAsync(SqlConnection connection)
        {
            await using var command = new SqlCommand("""
                DECLARE @result int;
                EXEC @result = sp_getapplock @Resource = N'ClinicFlow.OutboxDispatcher', @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0;
                SELECT @result
                """, connection);
            return (int)(await command.ExecuteScalarAsync())!;
        }

        private static async Task ReleaseLockAsync(SqlConnection connection)
        {
            await using var command = new SqlCommand("EXEC sp_releaseapplock @Resource = N'ClinicFlow.OutboxDispatcher', @LockOwner = 'Session'", connection);
            await command.ExecuteNonQueryAsync();
        }

        private async Task CancelAsAdminAsync()
        {
            _client.DefaultRequestHeaders.Clear();
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, Guid.NewGuid().ToString());
            _client.DefaultRequestHeaders.Add(TestAuthHandler.UserRoleHeader, "Admin");
            var response = await _client.PostAsJsonAsync($"/api/appointments/{_appointmentId}/cancel", new CancelAppointmentRequest("Clinic closed"));
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        private async Task ForgotPasswordAsync()
        {
            var response = await _client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest("patient@test.com"));
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        private async Task<List<OutboxMessage>> MessagesAsync()
        {
            List<OutboxMessage> messages = [];
            await factory.SeedAsync(async db => messages = await db.OutboxMessages.AsNoTracking().OrderBy(m => m.Id).ToListAsync());
            return messages;
        }

        private async Task<List<Notification>> NotificationsAsync()
        {
            List<Notification> notifications = [];
            await factory.SeedAsync(async db => notifications = await db.Notifications.AsNoTracking().ToListAsync());
            return notifications;
        }
    }
}
