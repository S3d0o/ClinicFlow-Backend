using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Persistence.AppData.Migrations
{
    /// <inheritdoc />
    public partial class AddEntitlements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrganizationModuleOverrides",
                columns: table => new
                {
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    ModuleCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationModuleOverrides", x => new { x.OrganizationId, x.ModuleCode });
                    table.ForeignKey(
                        name: "FK_OrganizationModuleOverrides_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Plans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsPublic = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Plans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationSubscriptions",
                columns: table => new
                {
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    PlanId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TrialEndsAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CurrentPeriodEnd = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationSubscriptions", x => x.OrganizationId);
                    table.ForeignKey(
                        name: "FK_OrganizationSubscriptions_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrganizationSubscriptions_Plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "Plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlanLimits",
                columns: table => new
                {
                    PlanId = table.Column<int>(type: "int", nullable: false),
                    LimitKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Value = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanLimits", x => new { x.PlanId, x.LimitKey });
                    table.ForeignKey(
                        name: "FK_PlanLimits_Plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "Plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlanModules",
                columns: table => new
                {
                    PlanId = table.Column<int>(type: "int", nullable: false),
                    ModuleCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanModules", x => new { x.PlanId, x.ModuleCode });
                    table.ForeignKey(
                        name: "FK_PlanModules_Plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "Plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "OrganizationModuleOverrides",
                columns: new[] { "ModuleCode", "OrganizationId", "Enabled" },
                values: new object[] { "REC", 1, true });

            migrationBuilder.InsertData(
                table: "Plans",
                columns: new[] { "Id", "Code", "IsPublic", "Name" },
                values: new object[,]
                {
                    { 1, "Starter", true, "Starter" },
                    { 2, "Clinic", true, "Clinic" },
                    { 3, "Full", true, "Full" }
                });

            migrationBuilder.InsertData(
                table: "OrganizationSubscriptions",
                columns: new[] { "OrganizationId", "CurrentPeriodEnd", "PlanId", "Status", "TrialEndsAt" },
                values: new object[] { 1, null, 3, "Active", null });

            migrationBuilder.InsertData(
                table: "PlanLimits",
                columns: new[] { "LimitKey", "PlanId", "Value" },
                values: new object[,]
                {
                    { "branches", 1, 1 },
                    { "doctors", 1, 2 },
                    { "phoneMessagesPerMonth", 1, 300 },
                    { "branches", 2, 1 },
                    { "doctors", 2, 5 },
                    { "phoneMessagesPerMonth", 2, 1000 },
                    { "branches", 3, 3 },
                    { "doctors", 3, 15 },
                    { "phoneMessagesPerMonth", 3, 2500 }
                });

            migrationBuilder.InsertData(
                table: "PlanModules",
                columns: new[] { "ModuleCode", "PlanId" },
                values: new object[,]
                {
                    { "BIL", 2 },
                    { "FD", 2 },
                    { "PAT", 2 },
                    { "RPT", 2 },
                    { "BIL", 3 },
                    { "EMR", 3 },
                    { "FD", 3 },
                    { "INS", 3 },
                    { "PAT", 3 },
                    { "RPT", 3 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationSubscriptions_PlanId",
                table: "OrganizationSubscriptions",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_Plans_Code",
                table: "Plans",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrganizationModuleOverrides");

            migrationBuilder.DropTable(
                name: "OrganizationSubscriptions");

            migrationBuilder.DropTable(
                name: "PlanLimits");

            migrationBuilder.DropTable(
                name: "PlanModules");

            migrationBuilder.DropTable(
                name: "Plans");
        }
    }
}
