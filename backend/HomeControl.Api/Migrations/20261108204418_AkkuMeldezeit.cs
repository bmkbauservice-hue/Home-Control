using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeControl.Api.Migrations
{
    /// <inheritdoc />
    public partial class AkkuMeldezeit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "BatteryReportedAtUtc",
                table: "Devices",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BatteryReportedAtUtc",
                table: "Devices");
        }
    }
}
