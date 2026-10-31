using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.AppData.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffMembers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "StaffMemberId",
                table: "Doctors",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StaffMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    IsMedicalDirector = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffMembers_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffMembers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StaffRoleAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    StaffMemberId = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    BranchId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffRoleAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffRoleAssignments_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffRoleAssignments_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffRoleAssignments_StaffMembers_StaffMemberId",
                        column: x => x.StaffMemberId,
                        principalTable: "StaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Doctors_StaffMemberId",
                table: "Doctors",
                column: "StaffMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffMembers_OrganizationId_UserId",
                table: "StaffMembers",
                columns: new[] { "OrganizationId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaffMembers_UserId",
                table: "StaffMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffRoleAssignments_BranchId",
                table: "StaffRoleAssignments",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffRoleAssignments_OrganizationId",
                table: "StaffRoleAssignments",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffRoleAssignments_StaffMemberId_Role_BranchId",
                table: "StaffRoleAssignments",
                columns: new[] { "StaffMemberId", "Role", "BranchId" },
                unique: true,
                filter: "[BranchId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Doctors_StaffMembers_StaffMemberId",
                table: "Doctors",
                column: "StaffMemberId",
                principalTable: "StaffMembers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Backfill (P1 PR 4): every Admin and Doctor user becomes an Active staff member of the default organization
            // (1), Admins organization-wide and Doctors at MAIN (branch 1), and each doctor profile points to its staff
            // member. From then on the token's staff roles come from these rows. Idempotent: users who already have a
            // staff member are skipped. Users stay Active whatever their account state; logging in still checks it
            migrationBuilder.Sql("""
                INSERT INTO [StaffMembers] ([OrganizationId], [UserId], [Status], [IsMedicalDirector])
                SELECT 1, u.[Id], N'Active', 0
                FROM [Users] u
                WHERE EXISTS (SELECT 1 FROM [UserRoles] ur JOIN [Roles] r ON r.[Id] = ur.[RoleId]
                              WHERE ur.[UserId] = u.[Id] AND r.[Name] IN (N'Admin', N'Doctor'))
                  AND NOT EXISTS (SELECT 1 FROM [StaffMembers] s WHERE s.[UserId] = u.[Id]);

                INSERT INTO [StaffRoleAssignments] ([OrganizationId], [StaffMemberId], [Role], [BranchId])
                SELECT s.[OrganizationId], s.[Id], r.[Name], CASE WHEN r.[Name] = N'Admin' THEN NULL ELSE 1 END
                FROM [StaffMembers] s
                JOIN [UserRoles] ur ON ur.[UserId] = s.[UserId]
                JOIN [Roles] r ON r.[Id] = ur.[RoleId]
                WHERE r.[Name] IN (N'Admin', N'Doctor')
                  AND NOT EXISTS (SELECT 1 FROM [StaffRoleAssignments] a WHERE a.[StaffMemberId] = s.[Id] AND a.[Role] = r.[Name]);

                UPDATE d SET d.[StaffMemberId] = s.[Id]
                FROM [Doctors] d JOIN [StaffMembers] s ON s.[UserId] = d.[UserId]
                WHERE d.[StaffMemberId] IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Doctors_StaffMembers_StaffMemberId",
                table: "Doctors");

            migrationBuilder.DropTable(
                name: "StaffRoleAssignments");

            migrationBuilder.DropTable(
                name: "StaffMembers");

            migrationBuilder.DropIndex(
                name: "IX_Doctors_StaffMemberId",
                table: "Doctors");

            migrationBuilder.DropColumn(
                name: "StaffMemberId",
                table: "Doctors");
        }
    }
}
