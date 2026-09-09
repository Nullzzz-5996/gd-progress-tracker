using FluentAssertions;
using GdTracker.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GdTracker.Tests;

/// <summary>
/// Обновление схемы работающего сервера. Проверяется главное: база, заведённая до
/// появления ников, продолжает работать, а её аккаунты получают уникальные имена —
/// иначе уникальный индекс не создастся и сервер не поднимется.
/// </summary>
public class SchemaUpgraderTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(), $"gdtracker-upgrade-tests-{Guid.NewGuid():N}.db");

    private string ConnectionString => $"Data Source={_databasePath}";

    [Fact]
    public async Task Accounts_from_the_version_without_usernames_get_unique_names()
    {
        // База прежней версии: ников нет, отображаемые имена уникальными не были.
        await CreateLegacyDatabaseAsync(
            ("first@example.com", "Игрок"),
            ("second@example.com", "Игрок"),
            ("third@example.com", ""));

        await using (var db = CreateContext())
        {
            await SchemaUpgrader.UpgradeAsync(db, NullLogger.Instance);
        }

        await using var check = CreateContext();
        var users = await check.Users.AsNoTracking().OrderBy(u => u.Email).ToListAsync();

        users.Should().HaveCount(3);
        users.Should().OnlyContain(u => u.Username.Length > 0);

        // Прежнее имя достаётся первому, второму — то же имя с номером.
        users.Select(u => u.Username).Should().Contain("Игрок");
        users.Select(u => u.UsernameNormalized).Should().OnlyHaveUniqueItems();

        // Аккаунту без имени ник выведен из почты, адрес целиком в него не попал.
        var third = users.Single(u => u.Email == "third@example.com");
        third.Username.Should().Be("third");
    }

    [Fact]
    public async Task Upgrade_is_safe_to_repeat()
    {
        await CreateLegacyDatabaseAsync(("only@example.com", "Соло"));

        await using (var db = CreateContext())
        {
            await SchemaUpgrader.UpgradeAsync(db, NullLogger.Instance);
            await SchemaUpgrader.UpgradeAsync(db, NullLogger.Instance);
        }

        await using var check = CreateContext();
        var user = await check.Users.AsNoTracking().SingleAsync();

        // Повторный проход не должен переименовывать аккаунт в «Соло2».
        user.Username.Should().Be("Соло");
    }

    private ApiDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(ConnectionString).Options);

    /// <summary>
    /// Создаёт базу такой, какой её оставила версия сервера до ников:
    /// таблица Users со столбцами DisplayName и Role, без Username.
    /// </summary>
    private async Task CreateLegacyDatabaseAsync(params (string Email, string DisplayName)[] users)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();

        await using (var create = connection.CreateCommand())
        {
            create.CommandText =
                """
                CREATE TABLE "Users" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_Users" PRIMARY KEY,
                    "Email" TEXT NOT NULL,
                    "PasswordHash" TEXT NOT NULL,
                    "CreatedAtUtc" TEXT NOT NULL,
                    "DisplayName" TEXT NOT NULL DEFAULT '',
                    "Role" INTEGER NOT NULL DEFAULT 0
                );
                """;
            await create.ExecuteNonQueryAsync();
        }

        foreach (var (email, displayName) in users)
        {
            await using var insert = connection.CreateCommand();
            insert.CommandText =
                """
                INSERT INTO "Users" ("Id", "Email", "PasswordHash", "CreatedAtUtc", "DisplayName", "Role")
                VALUES ($id, $email, 'hash', $created, $name, 0);
                """;
            // Идентификатор кладём объектом Guid, а не строкой: так его запишет тот
            // же драйвер и в том же виде, в каком его пишет EF на боевом сервере.
            insert.Parameters.AddWithValue("$id", Guid.NewGuid());
            insert.Parameters.AddWithValue("$email", email);
            insert.Parameters.AddWithValue("$created", DateTime.UtcNow.ToString("O"));
            insert.Parameters.AddWithValue("$name", displayName);
            await insert.ExecuteNonQueryAsync();
        }
    }

    public void Dispose()
    {
        // Соединения SQLite держатся в пуле: без сброса файл не удалить.
        SqliteConnection.ClearAllPools();

        try
        {
            if (File.Exists(_databasePath))
                File.Delete(_databasePath);
        }
        catch (IOException)
        {
            // Временный файл переживёт прогон — ронять из-за этого тесты незачем.
        }
    }
}
