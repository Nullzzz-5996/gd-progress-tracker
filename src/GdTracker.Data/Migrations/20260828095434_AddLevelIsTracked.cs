using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GdTracker.Data.Migrations
{
    /// <summary>
    /// Флаг видимости уровня в списке. Уровни, попавшие в базу импортом из игры, теперь
    /// хранятся скрытыми: их данные нужны статистике, но список они не засоряют.
    /// </summary>
    public partial class AddLevelIsTracked : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsTracked",
                table: "Levels",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            // Разбор уже накопленной базы: скрываем уровни, попавшие в неё импортом из игры,
            // то есть привязанные к GD ID и не тронутые пользователем вручную. Уровень
            // считается тронутым, если у него есть ручная запись прогресса или строка
            // личной таблицы вкладки «Прогрессы».
            migrationBuilder.Sql(@"
UPDATE Levels
SET IsTracked = 0
WHERE GdLevelId IS NOT NULL
  AND NOT EXISTS (
      SELECT 1 FROM ProgressRecords r
      WHERE r.LevelId = Levels.Id AND r.Source <> 'SaveImport')
  AND NOT EXISTS (
      SELECT 1 FROM LevelProgressRows p
      WHERE p.LevelId = Levels.Id);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsTracked",
                table: "Levels");
        }
    }
}
