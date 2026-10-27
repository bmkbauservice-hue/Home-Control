using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeControl.Api.Migrations
{
    /// <inheritdoc />
    public partial class TuyaGeraeteIdErgaenzt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TuyaDeviceId",
                table: "Devices",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Devices_TuyaDeviceId",
                table: "Devices",
                column: "TuyaDeviceId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Devices_TuyaDeviceId",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "TuyaDeviceId",
                table: "Devices");
        }
    }
}
