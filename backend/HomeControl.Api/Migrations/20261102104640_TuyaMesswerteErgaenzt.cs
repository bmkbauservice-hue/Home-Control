using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeControl.Api.Migrations
{
    /// <inheritdoc />
    public partial class TuyaMesswerteErgaenzt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BatteryPercent",
                table: "Devices",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "MeasuredAtUtc",
                table: "Devices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PowerWatt",
                table: "Devices",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "VoltageVolt",
                table: "Devices",
                type: "REAL",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BatteryPercent",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "MeasuredAtUtc",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "PowerWatt",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "VoltageVolt",
                table: "Devices");
        }
    }
}
