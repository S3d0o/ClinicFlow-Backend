using ClinicFlow.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Persistence.AppData.Migrations;

namespace ClinicFlow.IntegrationTests.Tests.Operations
{
    // What the app's login may do (P0 PR 11, REVIEW.md NM6, FR-AUD-04). docker/db-init.sql makes it a member of the
    // clinicflow_app role, which the AddAppDatabaseRole migration creates: read and write data, change no schema,
    // never update or delete an audit entry. Checked here with a login of the same shape against the real server
    [Collection(nameof(IntegrationTestCollection))]
    public class AppDatabaseRoleTests(ClinicFlowApiFactory factory) : IAsyncLifetime
    {
        private const string Login = "clinicflow_test_app";
        private const string Password = "AppRole@Test1234";
        private const int PermissionDenied = 229;  // an object permission was denied
        private const int CreateTableDenied = 262; // CREATE TABLE permission denied in the database

        private long _auditEntryId;

        public async Task InitializeAsync()
        {
            await factory.ResetDataBaseAsync();

            // As db-init.sql does, with the test server's admin connection
            await ExecuteAsync(factory.TestConnectionString, $"""
                IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'{Login}')
                    CREATE LOGIN [{Login}] WITH PASSWORD = N'{Password}', CHECK_POLICY = OFF;
                CREATE USER [{Login}] FOR LOGIN [{Login}];
                ALTER ROLE [{AddAppDatabaseRole.RoleName}] ADD MEMBER [{Login}];
                """);

            _auditEntryId = (long)(await ScalarAsync(factory.TestConnectionString, """
                INSERT INTO AuditEntries (OrganizationId, Action, EntityType, EntityId, Changes, CorrelationId)
                OUTPUT INSERTED.Id
                VALUES (1, 'Update', 'Appointment', '1', '{}', 'test');
                """))!;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task AppRole_CanReadAndAppendAuditEntries()
        {
            await ExecuteAsync(AppConnectionString, """
                INSERT INTO AuditEntries (OrganizationId, Action, EntityType, EntityId, Changes, CorrelationId)
                VALUES (1, 'Create', 'Appointment', '2', '{}', 'test');
                """);

            (await ScalarAsync(AppConnectionString, "SELECT COUNT(*) FROM AuditEntries")).Should().Be(2);
        }

        [Theory]
        [InlineData("UPDATE AuditEntries SET Reason = 'rewritten' WHERE Id = {0}")]
        [InlineData("DELETE FROM AuditEntries WHERE Id = {0}")]
        public async Task AppRole_CannotUpdateOrDeleteAuditEntries(string sql)
        {
            var act = () => ExecuteAsync(AppConnectionString, string.Format(sql, _auditEntryId));

            (await act.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(PermissionDenied);
            (await ScalarAsync(factory.TestConnectionString, "SELECT COUNT(*) FROM AuditEntries WHERE Reason IS NULL")).Should().Be(1);
        }

        [Fact]
        public async Task AppRole_CanUpdateAndDeleteOtherData()
        {
            var act = () => ExecuteAsync(AppConnectionString, """
                INSERT INTO Specialties (Name, IsActive) VALUES (N'Role test', 1);
                UPDATE Specialties SET IsActive = 0 WHERE Name = N'Role test';
                DELETE FROM Specialties WHERE Name = N'Role test';
                """);

            await act.Should().NotThrowAsync();
        }

        [Fact]
        public async Task AppRole_CannotChangeTheSchema()
        {
            var act = () => ExecuteAsync(AppConnectionString, "CREATE TABLE Sneaky (Id int)");

            (await act.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(CreateTableDenied);
        }

        private string AppConnectionString => new SqlConnectionStringBuilder(factory.TestConnectionString)
        {
            UserID = Login,
            Password = Password,
            Pooling = false, // the database is dropped between test classes
        }.ConnectionString;

        private static async Task ExecuteAsync(string connectionString, string sql)
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }

        private static async Task<object?> ScalarAsync(string connectionString, string sql)
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            return await command.ExecuteScalarAsync();
        }
    }
}
