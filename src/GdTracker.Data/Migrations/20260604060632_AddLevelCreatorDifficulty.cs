using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GdTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLevelCreatorDifficulty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Creator",
                table: "Levels",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Difficulty",
                table: "Levels",
                type: "TEXT",
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Creator",
                table: "Levels");

            migrationBuilder.DropColumn(
                name: "Difficulty",
                table: "Levels");
        }
    }
}
