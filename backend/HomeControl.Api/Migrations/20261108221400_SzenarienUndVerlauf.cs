using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeControl.Api.Migrations
{
    /// <inheritdoc />
    public partial class SzenarienUndVerlauf : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Scenarios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    TriggerDeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BlinkSystemId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    BlinkSystemName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Scenarios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Scenarios_Devices_TriggerDeviceId",
                        column: x => x.TriggerDeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScenarioRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScenarioId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    FinishedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Trigger = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScenarioRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScenarioRuns_Scenarios_ScenarioId",
                        column: x => x.ScenarioId,
                        principalTable: "Scenarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScenarioRuns_ScenarioId",
                table: "ScenarioRuns",
                column: "ScenarioId");

            migrationBuilder.CreateIndex(
                name: "IX_Scenarios_TriggerDeviceId",
                table: "Scenarios",
                column: "TriggerDeviceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScenarioRuns");

            migrationBuilder.DropTable(
                name: "Scenarios");
        }
    }
}
