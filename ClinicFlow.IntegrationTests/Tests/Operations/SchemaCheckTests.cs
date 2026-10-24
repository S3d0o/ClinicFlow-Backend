using ClinicFlow.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Persistence.AppData;

namespace ClinicFlow.IntegrationTests.Tests.Operations
{
    // Startup migrates only in development and tests (Database:MigrateOnStartup, docs/v2/plans/P0.md D1). Elsewhere
    // it checks that the migration bundle ran, and refuses to start against an older schema
    [Collection(nameof(IntegrationTestCollection))]
    public class SchemaCheckTests(ClinicFlowApiFactory factory) : IAsyncLifetime
    {
        public Task InitializeAsync() => factory.ResetDataBaseAsync();

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task EnsureSchema_WithoutMigrating_OnACurrentDatabase_Passes()
        {
            var act = () => factory.SeedAsync(db => DataSeeder.EnsureSchemaAsync(db, migrate: false, NullLogger.Instance));

            await act.Should().NotThrowAsync();
        }

        [Fact]
        public async Task EnsureSchema_WithoutMigrating_OnAnOlderDatabase_ThrowsNamingTheMissingMigration()
        {
            var latest = await RollBackTheLatestMigrationAsync(); // as if the bundle hadn't run yet

            var act = () => factory.SeedAsync(db => DataSeeder.EnsureSchemaAsync(db, migrate: false, NullLogger.Instance));

            (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage($"*{latest}*migrate service*");
        }

        [Fact]
        public async Task EnsureSchema_Migrating_BringsAnOlderDatabaseUpToDate()
        {
            await RollBackTheLatestMigrationAsync();

            await factory.SeedAsync(db => DataSeeder.EnsureSchemaAsync(db, migrate: true, NullLogger.Instance));

            await factory.SeedAsync(async db => (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty());
        }

        // Runs the latest migration's Down and returns its id
        private async Task<string> RollBackTheLatestMigrationAsync()
        {
            string latest = null!;
            await factory.SeedAsync(async db =>
            {
                var migrations = db.Database.GetMigrations().ToList();
                latest = migrations[^1];
                await db.GetService<IMigrator>().MigrateAsync(migrations[^2]);
            });
            return latest;
        }
    }
}
