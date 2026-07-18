using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GdTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountStatsSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountStatsSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CapturedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SaveFileWrittenAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Stars = table.Column<long>(type: "INTEGER", nullable: false),
                    Moons = table.Column<long>(type: "INTEGER", nullable: false),
                    Demons = table.Column<long>(type: "INTEGER", nullable: false),
                    OnlineLevelsCompleted = table.Column<long>(type: "INTEGER", nullable: false),
                    OfficialLevelsCompleted = table.Column<long>(type: "INTEGER", nullable: false),
                    SecretCoins = table.Column<long>(type: "INTEGER", nullable: false),
                    Attempts = table.Column<long>(type: "INTEGER", nullable: false),
                    Jumps = table.Column<long>(type: "INTEGER", nullable: false),
                    TotalOrbs = table.Column<long>(type: "INTEGER", nullable: false),
                    RawValuesJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountStatsSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountStatsSnapshots_CapturedAt",
                table: "AccountStatsSnapshots",
                column: "CapturedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountStatsSnapshots");
        }
    }
}
