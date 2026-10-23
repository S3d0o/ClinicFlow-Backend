using System.Data.Common;
using ClinicFlow.IntegrationTests.Infrastructure;
using Domain.Entities.IdentityModule;
using Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Persistence.AppData;
using Persistence.Tenancy;
using Persistence.Transactions;

namespace ClinicFlow.IntegrationTests.Tests.DataAccess
{
    // DbContextTransactionExtensions under a real retry strategy (REVIEW.md NL14). A retry re-runs the operation on the
    // same DbContext, so these check that the rewound change tracker makes it save exactly what one clean run would
    [Collection(nameof(IntegrationTestCollection))]
    public class TransactionRetryTests : IAsyncLifetime
    {
        // Not a real transient error number: the test strategy is told to retry it, so one THROW simulates a deadlock
        private const int SimulatedTransientError = 50001;

        private readonly ClinicFlowApiFactory _factory;
        private readonly FailOnceInterceptor _interceptor = new();
        private Guid _userId;

        public TransactionRetryTests(ClinicFlowApiFactory factory) => _factory = factory;

        public async Task InitializeAsync()
        {
            await _factory.ResetDataBaseAsync();
            await _factory.SeedAsync(async db => _userId = await SeedDataHelper.SeedPatientOnlyAsync(db, "retry"));
        }

        // A fragment that matches no command would let a test pass without simulating anything
        public Task DisposeAsync() => _interceptor.IsArmed
            ? throw new InvalidOperationException("The simulated transient error never fired: no command matched the SQL fragment.")
            : Task.CompletedTask;

        [Fact]
        public async Task ExecuteInTransactionAsync_WhenTheInsertFailsTransiently_RetriesAndInsertsOnce()
        {
            await using var db = NewContext();
            _interceptor.FailNext("INSERT INTO [Notifications]");
            var attempts = 0;

            await db.ExecuteInTransactionAsync(async transaction =>
            {
                attempts++;
                db.Notifications.Add(NewNotification("Booked"));
                await db.SaveChangesAsync();
                await transaction.CommitAsync(CancellationToken.None);
            }, CancellationToken.None);

            attempts.Should().Be(2);
            (await CountNotificationsAsync()).Should().Be(1);
        }

        [Fact]
        public async Task ExecuteInTransactionAsync_WhenAStatementAfterTheSaveFails_RollsBackAndInsertsOnceOnRetry()
        {
            // The first attempt's insert succeeded, and gave the entity an identity value, before the rollback
            await using var db = NewContext();
            _interceptor.FailNext("FROM [Users]");
            var attempts = 0;
            Notification? added = null;

            await db.ExecuteInTransactionAsync(async transaction =>
            {
                attempts++;
                added = NewNotification("Booked");
                db.Notifications.Add(added);
                await db.SaveChangesAsync();
                await db.Users.CountAsync();
                await transaction.CommitAsync(CancellationToken.None);
            }, CancellationToken.None);

            attempts.Should().Be(2);
            (await CountNotificationsAsync()).Should().Be(1);
            (await NotificationIdsAsync()).Should().Equal(added!.Id);
        }

        [Fact]
        public async Task ExecuteInTransactionAsync_WhenTheSameInstanceIsAddedAgainOnRetry_InsertsItOnce()
        {
            // Services build the entity before the transaction and add it inside (ReviewService, AuthService)
            await using var db = NewContext();
            _interceptor.FailNext("FROM [Users]");
            var notification = NewNotification("Created outside");

            await db.ExecuteInTransactionAsync(async transaction =>
            {
                db.Notifications.Add(notification);
                await db.SaveChangesAsync();
                await db.Users.CountAsync();
                await transaction.CommitAsync(CancellationToken.None);
            }, CancellationToken.None);

            (await CountNotificationsAsync()).Should().Be(1);
            (await NotificationIdsAsync()).Should().Equal(notification.Id);
        }

        [Fact]
        public async Task ExecuteInTransactionAsync_WhenAPrincipalIsTrackedWithItsCollection_InsertsTheNewDependentOnce()
        {
            // EF adds the new notification to the tracked user's Notifications collection; the failed attempt's
            // instance must not come back from there on the retry
            await using var db = NewContext();
            var user = await db.Users.Include(u => u.Notifications).SingleAsync(u => u.Id == _userId);
            _interceptor.FailNext("FROM [Users]");

            await db.ExecuteInTransactionAsync(async transaction =>
            {
                db.Notifications.Add(NewNotification("Booked"));
                await db.SaveChangesAsync();
                await db.Users.CountAsync();
                await transaction.CommitAsync(CancellationToken.None);
            }, CancellationToken.None);

            (await CountNotificationsAsync()).Should().Be(1);
            db.ChangeTracker.HasChanges().Should().BeFalse();
        }

        [Fact]
        public async Task ExecuteInTransactionAsync_WhenAChangeMadeBeforeItWasSavedInTheFailedAttempt_SavesItOnRetry()
        {
            // DoctorService.UpdateScheduleAsync: the schedule is changed before the transaction and saved inside it.
            // Without the rewind, the retry would see it as already saved and lose the change
            var id = await SeedNotificationAsync("Before");
            await using var db = NewContext();
            var notification = await db.Notifications.SingleAsync(n => n.Id == id);
            notification.Title = "After";
            _interceptor.FailNext("FROM [Users]");

            await db.ExecuteInTransactionAsync(async transaction =>
            {
                await db.SaveChangesAsync();
                await db.Users.CountAsync();
                await transaction.CommitAsync(CancellationToken.None);
            }, CancellationToken.None);

            (await TitleAsync(id)).Should().Be("After");
        }

        [Fact]
        public async Task ExecuteInTransactionAsync_WhenADeleteIsRetried_DeletesTheRow()
        {
            // ReviewService.DeleteReviewAsync: SaveChanges detaches the deleted entity; the retry deletes it again
            var id = await SeedNotificationAsync("Doomed");
            await using var db = NewContext();
            var notification = await db.Notifications.SingleAsync(n => n.Id == id);
            _interceptor.FailNext("FROM [Users]");

            await db.ExecuteInTransactionAsync(async transaction =>
            {
                db.Notifications.Remove(notification);
                await db.SaveChangesAsync();
                await db.Users.CountAsync();
                await transaction.CommitAsync(CancellationToken.None);
            }, CancellationToken.None);

            (await CountNotificationsAsync()).Should().Be(0);
        }

        [Fact]
        public async Task ExecuteInTransactionAsync_WhenTheOperationReturnsWithoutCommitting_RollsBackAndForgetsItsChanges()
        {
            await using var db = NewContext();

            var result = await db.ExecuteInTransactionAsync(async _ =>
            {
                db.Notifications.Add(NewNotification("Never committed"));
                await db.SaveChangesAsync();
                return "rejected";
            }, CancellationToken.None);

            result.Should().Be("rejected");
            (await CountNotificationsAsync()).Should().Be(0);
            db.ChangeTracker.Entries<Notification>().Should().BeEmpty();
        }

        [Fact]
        public async Task ExecuteInTransactionAsync_WhenAStatementFailsAfterTheCommit_DoesNotRetry()
        {
            // Re-running a committed operation would apply it twice
            await using var db = NewContext();
            _interceptor.FailNext("FROM [Users]");
            var attempts = 0;

            var act = () => db.ExecuteInTransactionAsync(async transaction =>
            {
                attempts++;
                db.Notifications.Add(NewNotification("Committed"));
                await db.SaveChangesAsync();
                await transaction.CommitAsync(CancellationToken.None);
                await db.Users.CountAsync();
            }, CancellationToken.None);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*committed*");
            attempts.Should().Be(1);
            (await CountNotificationsAsync()).Should().Be(1);
        }

        [Fact]
        public async Task ExecuteInTransactionAsync_WhenTheErrorIsNotTransient_RollsBackWithoutRetrying()
        {
            await using var db = NewContext();
            var attempts = 0;

            var act = () => db.ExecuteInTransactionAsync(async transaction =>
            {
                attempts++;
                db.Notifications.Add(NewNotification("Saved, then rolled back"));
                await db.SaveChangesAsync();
                throw new InvalidOperationException("business failure");
            }, CancellationToken.None);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("business failure");
            attempts.Should().Be(1);
            (await CountNotificationsAsync()).Should().Be(0);
        }

        private ClinicDbContext NewContext() => new(new DbContextOptionsBuilder<ClinicDbContext>()
            .UseSqlServer(_factory.TestConnectionString, sql => sql.EnableRetryOnFailure(
                maxRetryCount: 3, maxRetryDelay: TimeSpan.FromMilliseconds(10), errorNumbersToAdd: [SimulatedTransientError]))
            .AddInterceptors(_interceptor)
            .Options, new SingleOrganizationTenantContext());

        private Notification NewNotification(string title) => new()
        {
            UserId = _userId,
            Title = title,
            Message = "Integration test",
            Type = NotificationType.SystemAlert,
        };

        private async Task<int> SeedNotificationAsync(string title)
        {
            var id = 0;
            await _factory.SeedAsync(async db =>
            {
                var notification = NewNotification(title);
                db.Notifications.Add(notification);
                await db.SaveChangesAsync();
                id = notification.Id;
            });
            return id;
        }

        private async Task<int> CountNotificationsAsync()
        {
            await using var db = NewContext();
            return await db.Notifications.CountAsync();
        }

        private async Task<List<int>> NotificationIdsAsync()
        {
            await using var db = NewContext();
            return await db.Notifications.Select(n => n.Id).ToListAsync();
        }

        private async Task<string> TitleAsync(int id)
        {
            await using var db = NewContext();
            return await db.Notifications.Where(n => n.Id == id).Select(n => n.Title).SingleAsync();
        }

        // Replaces the next command containing the given SQL with a THROW of the simulated transient error
        private sealed class FailOnceInterceptor : DbCommandInterceptor
        {
            private string? _sqlFragment;

            public void FailNext(string sqlFragment) => _sqlFragment = sqlFragment;

            public bool IsArmed => _sqlFragment is not null;

            public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
                DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
            {
                FailIfArmed(command);
                return base.ReaderExecutingAsync(command, eventData, result, ct);
            }

            public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
                DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken ct = default)
            {
                FailIfArmed(command);
                return base.ScalarExecutingAsync(command, eventData, result, ct);
            }

            public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
                DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
            {
                FailIfArmed(command);
                return base.NonQueryExecutingAsync(command, eventData, result, ct);
            }

            private void FailIfArmed(DbCommand command)
            {
                if (_sqlFragment is null || !command.CommandText.Contains(_sqlFragment))
                    return;

                _sqlFragment = null;
                command.CommandText = $"THROW {SimulatedTransientError}, 'Simulated transient error', 1;";
            }
        }
    }
}
