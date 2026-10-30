using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.AppData.Migrations
{
    /// <inheritdoc />
    public partial class AddRoomsAndOrganizationProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "BrandName",
                table: "Organizations",
                newName: "BrandNameEn");

            migrationBuilder.RenameColumn(
                name: "Address",
                table: "Branches",
                newName: "AddressEn");

            migrationBuilder.AddColumn<string>(
                name: "BrandNameAr",
                table: "Organizations",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Organizations",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "Organizations",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressAr",
                table: "Branches",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Rooms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    BranchId = table.Column<int>(type: "int", nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rooms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Rooms_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Rooms_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "Branches",
                keyColumn: "Id",
                keyValue: 1,
                column: "AddressAr",
                value: null);

            migrationBuilder.UpdateData(
                table: "Organizations",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "BrandNameAr", "Email", "Phone" },
                values: new object[] { "كلينك فلو", null, null });

            migrationBuilder.CreateIndex(
                name: "IX_Rooms_BranchId_NameAr",
                table: "Rooms",
                columns: new[] { "BranchId", "NameAr" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Rooms_BranchId_NameEn",
                table: "Rooms",
                columns: new[] { "BranchId", "NameEn" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Rooms_OrganizationId",
                table: "Rooms",
                column: "OrganizationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Rooms");

            migrationBuilder.DropColumn(
                name: "BrandNameAr",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "AddressAr",
                table: "Branches");

            migrationBuilder.RenameColumn(
                name: "BrandNameEn",
                table: "Organizations",
                newName: "BrandName");

            migrationBuilder.RenameColumn(
                name: "AddressEn",
                table: "Branches",
                newName: "Address");
        }
    }
}
