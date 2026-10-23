using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.AppData.Migrations
{
    /// <inheritdoc />
    // Slots keep their branch-local Date/StartTime/EndTime and gain the same instants in UTC (docs/v2/adr/0003).
    // Existing slots are converted in SQL with AT TIME ZONE, which takes Windows zone names, while branches store IANA
    // ids; the mapping comes from .NET's zone data when the migration runs. A branch zone with no mapping stops the
    // migration rather than leave slots without instants. AddSlotUtcTimesTests compares the result with .NET's conversion.
    public partial class AddSlotUtcTimes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EndsAtUtc",
                table: "AppointmentSlots",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartsAtUtc",
                table: "AppointmentSlots",
                type: "datetime2",
                nullable: true);

            migrationBuilder.Sql($"""
                UPDATE s SET
                    StartsAtUtc = CAST((CAST(CONCAT(s.[Date], ' ', s.[StartTime]) AS datetime2) AT TIME ZONE z.WindowsZone) AT TIME ZONE 'UTC' AS datetime2),
                    EndsAtUtc   = CAST((CAST(CONCAT(s.[Date], ' ', s.[EndTime])   AS datetime2) AT TIME ZONE z.WindowsZone) AT TIME ZONE 'UTC' AS datetime2)
                FROM AppointmentSlots s
                JOIN Branches b ON b.Id = s.BranchId
                CROSS APPLY (SELECT CASE b.TimeZoneId
                {WindowsZoneCases()}
                    END AS WindowsZone) z
                WHERE z.WindowsZone IS NOT NULL;

                IF EXISTS (SELECT 1 FROM AppointmentSlots WHERE StartsAtUtc IS NULL OR EndsAtUtc IS NULL)
                    THROW 50000, N'AddSlotUtcTimes: a branch time zone has no SQL Server (Windows) equivalent; fix Branches.TimeZoneId and run the migration again.', 1;
                """);

            migrationBuilder.AlterColumn<DateTime>(
                name: "StartsAtUtc",
                table: "AppointmentSlots",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "EndsAtUtc",
                table: "AppointmentSlots",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentSlots_StartsAtUtc",
                table: "AppointmentSlots",
                column: "StartsAtUtc");
        }

        // "WHEN N'Africa/Cairo' THEN N'Egypt Standard Time'" for every zone .NET knows, by IANA id and by Windows id
        private static string WindowsZoneCases()
        {
            var windowsByZoneId = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var zone in TimeZoneInfo.GetSystemTimeZones())
            {
                if (zone.HasIanaId)
                {
                    if (TimeZoneInfo.TryConvertIanaIdToWindowsId(zone.Id, out var windowsId))
                        windowsByZoneId[zone.Id] = windowsId;
                }
                else
                {
                    windowsByZoneId[zone.Id] = zone.Id;
                    if (TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var ianaId))
                        windowsByZoneId[ianaId] = zone.Id;
                }
            }

            static string Quote(string value) => value.Replace("'", "''");
            return string.Join(Environment.NewLine,
                windowsByZoneId.Select(z => $"    WHEN N'{Quote(z.Key)}' THEN N'{Quote(z.Value)}'"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppointmentSlots_StartsAtUtc",
                table: "AppointmentSlots");

            migrationBuilder.DropColumn(
                name: "EndsAtUtc",
                table: "AppointmentSlots");

            migrationBuilder.DropColumn(
                name: "StartsAtUtc",
                table: "AppointmentSlots");
        }
    }
}
