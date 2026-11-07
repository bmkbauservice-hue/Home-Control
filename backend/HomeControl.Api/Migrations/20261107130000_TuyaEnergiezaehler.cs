using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeControl.Api.Migrations;

public partial class TuyaEnergiezaehler : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<double>(
            name: "EstimatedEnergyKwh", table: "Devices", type: "REAL", nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "PowerMeasuredAtUtc", table: "Devices", type: "TEXT", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "EstimatedEnergyKwh", table: "Devices");
        migrationBuilder.DropColumn(name: "PowerMeasuredAtUtc", table: "Devices");
    }
}
