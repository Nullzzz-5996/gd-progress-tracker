using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GdTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLevelProgressRow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LevelProgressRows",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LevelId = table.Column<int>(type: "INTEGER", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    PracticeAttempts = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    SegmentRange = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    ToHundredRange = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LevelProgressRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LevelProgressRows_Levels_LevelId",
                        column: x => x.LevelId,
                        principalTable: "Levels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LevelProgressRows_LevelId",
                table: "LevelProgressRows",
                column: "LevelId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LevelProgressRows");
        }
    }
}
