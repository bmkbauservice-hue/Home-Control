using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeControl.Api.Migrations
{
    /// <inheritdoc />
    public partial class BlinkAnmeldedatenErgaenzt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BlinkCredentials",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HardwareId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    EncryptedRefreshToken = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlinkCredentials", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BlinkCredentials");
        }
    }
}
