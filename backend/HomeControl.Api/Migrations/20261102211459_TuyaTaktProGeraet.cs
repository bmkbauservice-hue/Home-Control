using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeControl.Api.Migrations
{
    /// <inheritdoc />
    public partial class TuyaTaktProGeraet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TuyaSyncSeconds",
                table: "Devices",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TuyaSyncSeconds",
                table: "Devices");
        }
    }
}
