using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.AppData.Migrations
{
    /// <summary>
    /// The database role the app's login belongs to (P0 PR 11, REVIEW.md NM6): it reads and writes data but changes
    /// no schema, and it can't update or delete audit entries (FR-AUD-04). Kept in a migration, so wherever the schema
    /// exists the rule exists with it. docker/db-init.sql makes the app's login a member; the deploy login that runs
    /// migrations is db_owner. A database whose app connects as db_owner or sa (development) is unaffected.
    /// </summary>
    public partial class AddAppDatabaseRole : Migration
    {
        public const string RoleName = "clinicflow_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // db-init.sql may have created the role already, so its first run can add the login before any migration
            migrationBuilder.Sql($"IF DATABASE_PRINCIPAL_ID(N'{RoleName}') IS NULL CREATE ROLE [{RoleName}];");
            migrationBuilder.Sql($"ALTER ROLE [db_datareader] ADD MEMBER [{RoleName}];");
            migrationBuilder.Sql($"ALTER ROLE [db_datawriter] ADD MEMBER [{RoleName}];");
            // A DENY wins over db_datawriter's grant. INSERT and SELECT stay allowed
            migrationBuilder.Sql($"DENY UPDATE, DELETE ON [dbo].[AuditEntries] TO [{RoleName}];");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"REVOKE UPDATE, DELETE ON [dbo].[AuditEntries] FROM [{RoleName}];");
            migrationBuilder.Sql($"ALTER ROLE [db_datawriter] DROP MEMBER [{RoleName}];");
            migrationBuilder.Sql($"ALTER ROLE [db_datareader] DROP MEMBER [{RoleName}];");
            // The role keeps any login db-init.sql added; it is only dropped when nobody is a member
            migrationBuilder.Sql($"""
                IF NOT EXISTS (SELECT 1 FROM sys.database_role_members WHERE role_principal_id = DATABASE_PRINCIPAL_ID(N'{RoleName}'))
                    DROP ROLE [{RoleName}];
                """);
        }
    }
}
