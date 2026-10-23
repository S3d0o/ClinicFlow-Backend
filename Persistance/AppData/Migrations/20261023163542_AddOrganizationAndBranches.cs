using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.AppData.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationAndBranches : Migration
    {
        /// <inheritdoc />
        // Existing rows (the V2 demo data) belong to the organization and MAIN branch inserted below, both id 1, so the new
        // columns default to 1; the foreign keys are added after the rows exist. EF always writes these columns itself
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "Reviews",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "Notifications",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "DoctorSchedules",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "DoctorSchedules",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "Doctors",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "AppointmentSlots",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "AppointmentSlots",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "Appointments",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "Appointments",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "Organizations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LegalName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BrandName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LogoUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TaxRegistrationNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CommercialRegistrationNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DefaultTimeZoneId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organizations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Branches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Latitude = table.Column<double>(type: "float", nullable: true),
                    Longitude = table.Column<double>(type: "float", nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    TimeZoneId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Branches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Branches_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Organizations",
                columns: new[] { "Id", "BrandName", "CommercialRegistrationNo", "DefaultTimeZoneId", "IsActive", "LegalName", "LogoUrl", "TaxRegistrationNo" },
                values: new object[] { 1, "ClinicFlow", null, "Africa/Cairo", true, "ClinicFlow", null, null });

            migrationBuilder.InsertData(
                table: "Branches",
                columns: new[] { "Id", "Address", "City", "Code", "IsActive", "Latitude", "Longitude", "NameAr", "NameEn", "OrganizationId", "Phone", "TimeZoneId" },
                values: new object[] { 1, null, null, "MAIN", true, null, null, "الفرع الرئيسي", "Main branch", 1, null, "Africa/Cairo" });

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_OrganizationId",
                table: "Reviews",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_OrganizationId",
                table: "Notifications",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorSchedules_BranchId",
                table: "DoctorSchedules",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorSchedules_OrganizationId",
                table: "DoctorSchedules",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Doctors_OrganizationId",
                table: "Doctors",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentSlots_BranchId",
                table: "AppointmentSlots",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentSlots_OrganizationId",
                table: "AppointmentSlots",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_BranchId",
                table: "Appointments",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_OrganizationId",
                table: "Appointments",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Branches_OrganizationId_Code",
                table: "Branches",
                columns: new[] { "OrganizationId", "Code" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_Branches_BranchId",
                table: "Appointments",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_Organizations_OrganizationId",
                table: "Appointments",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AppointmentSlots_Branches_BranchId",
                table: "AppointmentSlots",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AppointmentSlots_Organizations_OrganizationId",
                table: "AppointmentSlots",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Doctors_Organizations_OrganizationId",
                table: "Doctors",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DoctorSchedules_Branches_BranchId",
                table: "DoctorSchedules",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DoctorSchedules_Organizations_OrganizationId",
                table: "DoctorSchedules",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Notifications_Organizations_OrganizationId",
                table: "Notifications",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Reviews_Organizations_OrganizationId",
                table: "Reviews",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_Branches_BranchId",
                table: "Appointments");

            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_Organizations_OrganizationId",
                table: "Appointments");

            migrationBuilder.DropForeignKey(
                name: "FK_AppointmentSlots_Branches_BranchId",
                table: "AppointmentSlots");

            migrationBuilder.DropForeignKey(
                name: "FK_AppointmentSlots_Organizations_OrganizationId",
                table: "AppointmentSlots");

            migrationBuilder.DropForeignKey(
                name: "FK_Doctors_Organizations_OrganizationId",
                table: "Doctors");

            migrationBuilder.DropForeignKey(
                name: "FK_DoctorSchedules_Branches_BranchId",
                table: "DoctorSchedules");

            migrationBuilder.DropForeignKey(
                name: "FK_DoctorSchedules_Organizations_OrganizationId",
                table: "DoctorSchedules");

            migrationBuilder.DropForeignKey(
                name: "FK_Notifications_Organizations_OrganizationId",
                table: "Notifications");

            migrationBuilder.DropForeignKey(
                name: "FK_Reviews_Organizations_OrganizationId",
                table: "Reviews");

            migrationBuilder.DropTable(
                name: "Branches");

            migrationBuilder.DropTable(
                name: "Organizations");

            migrationBuilder.DropIndex(
                name: "IX_Reviews_OrganizationId",
                table: "Reviews");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_OrganizationId",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_DoctorSchedules_BranchId",
                table: "DoctorSchedules");

            migrationBuilder.DropIndex(
                name: "IX_DoctorSchedules_OrganizationId",
                table: "DoctorSchedules");

            migrationBuilder.DropIndex(
                name: "IX_Doctors_OrganizationId",
                table: "Doctors");

            migrationBuilder.DropIndex(
                name: "IX_AppointmentSlots_BranchId",
                table: "AppointmentSlots");

            migrationBuilder.DropIndex(
                name: "IX_AppointmentSlots_OrganizationId",
                table: "AppointmentSlots");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_BranchId",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_OrganizationId",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "Reviews");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "DoctorSchedules");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "DoctorSchedules");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "Doctors");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "AppointmentSlots");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "AppointmentSlots");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "Appointments");
        }
    }
}
