using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeControl.Api.Migrations
{
    /// <inheritdoc />
    public partial class GoogleHomeVerknuepfung : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GoogleHomeDeviceId",
                table: "Devices",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Devices_GoogleHomeDeviceId",
                table: "Devices",
                column: "GoogleHomeDeviceId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Devices_GoogleHomeDeviceId",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "GoogleHomeDeviceId",
                table: "Devices");
        }
    }
}
