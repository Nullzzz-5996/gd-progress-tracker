using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GdTracker.Api.Data;

/// <summary>
/// Досоздание того, чего нет в уже работающей базе.
///
/// Сервер поднимает схему через <c>EnsureCreated</c>, а он умеет ровно одно:
/// создать базу целиком, если её ещё нет. На сервере, который уже работал до
/// появления демонлиста, база есть — и он молча оставил бы её без новых таблиц
/// и без новых столбцов в <c>Users</c>, после чего сломался бы даже вход.
/// Миграций в проекте нет, поэтому недостающее добирается здесь.
///
/// Приём безопасно повторять: на полной базе все шаги — пустые.
/// </summary>
public static class SchemaUpgrader
{
    /// <summary>Столбцы, добавленные к <c>Users</c> после первой версии сервера.</summary>
    private static readonly (string Name, string Definition)[] UserColumns =
    [
        // DisplayName остался от версии до ников: столбец никуда не делся (SQLite
        // не любит удалять столбцы), но модель его больше не знает — значения
        // переехали в Username при первом запуске.
        ("DisplayName", "TEXT NOT NULL DEFAULT ''"),
        ("Role", "INTEGER NOT NULL DEFAULT 0"),
        ("Username", "TEXT NOT NULL DEFAULT ''"),
        ("UsernameNormalized", "TEXT NOT NULL DEFAULT ''"),
        // Баны появились вместе с ролью владельца.
        ("IsBanned", "INTEGER NOT NULL DEFAULT 0"),
        ("BanReason", "TEXT NULL"),
        ("BannedAtUtc", "TEXT NULL"),
        ("IsListBanned", "INTEGER NOT NULL DEFAULT 0"),
        ("ListBanReason", "TEXT NULL"),
        ("ListBannedAtUtc", "TEXT NULL"),
    ];

    /// <summary>Дополняет схему до текущей модели. Данные не трогает.</summary>
    public static async Task UpgradeAsync(ApiDbContext db, ILogger logger, CancellationToken ct = default)
    {
        // Порядок важен: сначала столбцы, потом таблицы и индексы. Уникальный
        // индекс по нику ссылается на столбец Username, а заполнить ники нужно
        // до его создания — иначе все пустые значения столкнутся друг с другом.
        await AddMissingUserColumnsAsync(db, logger, ct);
        await FillMissingUsernamesAsync(db, logger, ct);
        await AddMissingTablesAsync(db, logger, ct);
    }

    /// <summary>
    /// Выдаёт ники аккаунтам, заведённым до их появления: берёт прежнее
    /// отображаемое имя, а если его не было — часть почты до собачки.
    /// </summary>
    private static async Task FillMissingUsernamesAsync(ApiDbContext db, ILogger logger, CancellationToken ct)
    {
        var users = await db.Users.Where(u => u.Username == string.Empty).ToListAsync(ct);
        if (users.Count == 0)
            return;

        var legacyNames = await ReadLegacyDisplayNamesAsync(db, ct);
        var taken = await db.Users
            .Where(u => u.Username != string.Empty)
            .Select(u => u.UsernameNormalized)
            .ToListAsync(ct);
        var used = new HashSet<string>(taken, StringComparer.Ordinal);

        foreach (var user in users)
        {
            var wanted = legacyNames.GetValueOrDefault(user.Id) is { Length: > 0 } legacy
                         && Usernames.Validate(legacy) is null
                ? legacy.Trim()
                : await Usernames.DeriveAsync(db, user.Email, ct);

            // Прежние отображаемые имена уникальными не были: столкнувшиеся
            // разводим номером, иначе уникальный индекс не создастся.
            var candidate = wanted;
            for (var suffix = 2; !used.Add(Usernames.Normalize(candidate)); suffix++)
            {
                var tail = suffix.ToString();
                candidate = wanted[..Math.Min(wanted.Length, Usernames.MaxLength - tail.Length)] + tail;
            }

            user.Username = candidate;
            user.UsernameNormalized = Usernames.Normalize(candidate);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Ники выданы аккаунтам без имени: {Count}.", users.Count);
    }

    /// <summary>
    /// Читает прежний столбец DisplayName напрямую: в модели его больше нет,
    /// а на старой базе он ещё хранит выбранные людьми имена.
    /// </summary>
    private static async Task<Dictionary<Guid, string>> ReadLegacyDisplayNamesAsync(
        ApiDbContext db, CancellationToken ct)
    {
        var result = new Dictionary<Guid, string>();

        var connection = (SqliteConnection)db.Database.GetDbConnection();
        var opened = connection.State != System.Data.ConnectionState.Open;
        if (opened) await connection.OpenAsync(ct);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, DisplayName FROM \"Users\" WHERE DisplayName <> '';";
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                // GetGuid, а не разбор строки: тот же драйвер хранит идентификаторы
                // то текстом, то блобом, и знает, как прочитать оба варианта.
                result[reader.GetGuid(0)] = reader.GetString(1);
            }
        }
        catch (SqliteException)
        {
            // Столбца нет вовсе (совсем свежая база) — переносить нечего.
        }
        finally
        {
            if (opened) await connection.CloseAsync();
        }

        return result;
    }

    /// <summary>
    /// Прогоняет скрипт создания схемы по частям. Существующие таблицы отзываются
    /// ошибкой «уже есть» — её и пропускаем; отсутствующие создаются. Так новые
    /// таблицы описаны ровно один раз — самой моделью, а не копией её DDL здесь.
    /// </summary>
    private static async Task AddMissingTablesAsync(ApiDbContext db, ILogger logger, CancellationToken ct)
    {
        foreach (var statement in Statements(db.Database.GenerateCreateScript()))
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(statement, ct);
                logger.LogInformation("Схема дополнена: {Statement}", FirstLine(statement));
            }
            catch (SqliteException e) when (AlreadyExists(e))
            {
                // Таблица или индекс на месте — так и должно быть на полной базе.
            }
        }
    }

    private static async Task AddMissingUserColumnsAsync(ApiDbContext db, ILogger logger, CancellationToken ct)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var connection = (SqliteConnection)db.Database.GetDbConnection();
        var opened = connection.State != System.Data.ConnectionState.Open;
        if (opened) await connection.OpenAsync(ct);

        try
        {
            await using (var probe = connection.CreateCommand())
            {
                probe.CommandText = "SELECT name FROM pragma_table_info('Users');";
                await using var reader = await probe.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct)) existing.Add(reader.GetString(0));
            }

            // Пустой набор — таблицы Users нет вовсе, её только что создал шаг выше
            // вместе со всеми столбцами; дополнять нечего.
            if (existing.Count == 0) return;

            foreach (var (name, definition) in UserColumns)
            {
                if (existing.Contains(name)) continue;

                await using var alter = connection.CreateCommand();
                // Имя и определение столбца — константы этого файла, не пользовательский ввод.
                alter.CommandText = $"ALTER TABLE \"Users\" ADD COLUMN \"{name}\" {definition};";
                await alter.ExecuteNonQueryAsync(ct);
                logger.LogInformation("В таблицу Users добавлен столбец {Column}.", name);
            }
        }
        finally
        {
            if (opened) await connection.CloseAsync();
        }
    }

    /// <summary>Разбивает скрипт EF на отдельные команды.</summary>
    private static IEnumerable<string> Statements(string script) =>
        script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.Length > 0);

    private static bool AlreadyExists(SqliteException e) =>
        e.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase);

    private static string FirstLine(string statement)
    {
        var end = statement.IndexOf('\n');
        return (end < 0 ? statement : statement[..end]).Trim();
    }
}
