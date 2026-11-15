using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Weather.Infrastructure.WeatherHistory.Migrations
{
    /// <inheritdoc />
    public partial class AlignStoredWeatherSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Refuse silent Unicode or timestamp precision loss when upgrading an old database.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM dbo.DeviceData WHERE
                    CONVERT(varbinary(max), CONVERT(nvarchar(100), Loc)) <>
                    CONVERT(varbinary(max), CONVERT(nvarchar(100), CONVERT(varchar(100), Loc))))
                    THROW 50002, 'Schema alignment would lose location characters. Review data before migrating.', 1;
                IF EXISTS (SELECT 1 FROM dbo.DeviceData WHERE TRY_CONVERT(datetime, [Date]) IS NULL
                    OR CONVERT(datetime2, TRY_CONVERT(datetime, [Date])) <> CONVERT(datetime2, [Date]))
                    THROW 50003, 'Schema alignment would lose timestamp range or precision. Review data before migrating.', 1;
                """);

            migrationBuilder.EnsureSchema(
                name: "dbo");

            migrationBuilder.RenameTable(
                name: "DeviceData",
                newName: "DeviceData",
                newSchema: "dbo");

            migrationBuilder.AlterColumn<string>(
                name: "Loc",
                schema: "dbo",
                table: "DeviceData",
                type: "varchar(100)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<DateTime>(
                name: "Date",
                schema: "dbo",
                table: "DeviceData",
                type: "datetime",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "DeviceData",
                schema: "dbo",
                newName: "DeviceData");

            migrationBuilder.AlterColumn<string>(
                name: "Loc",
                table: "DeviceData",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(100)");

            migrationBuilder.AlterColumn<DateTime>(
                name: "Date",
                table: "DeviceData",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime");
        }
    }
}
