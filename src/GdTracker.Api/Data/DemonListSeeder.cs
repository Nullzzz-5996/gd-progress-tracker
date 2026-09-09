using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace GdTracker.Api.Data;

/// <summary>Одна позиция стартовой расстановки из файла сида.</summary>
internal sealed record DemonSeedEntry(
    int Position,
    string Name,
    string? Publisher,
    string? Verifier,
    long? LevelId,
    string? Video,
    string? Thumbnail,
    int? Requirement);

internal sealed record DemonSeedFile(
    string? Source,
    DateTime? FetchedAtUtc,
    [property: JsonPropertyName("entries")] IReadOnlyList<DemonSeedEntry> Entries);

/// <summary>
/// Первоначальное наполнение демонлиста. Расстановка берётся из снимка
/// глобального демонлиста (pointercrate.com), вложенного в сборку ресурсом;
/// снимок обновляется скриптом <c>tools/fetch-demonlist.mjs</c>.
///
/// Сид накатывается только на пустой список: как только модераторы начали
/// двигать демонов, их работу перезаписывать нельзя.
/// </summary>
public static class DemonListSeeder
{
    private const string ResourceName = "GdTracker.Api.Data.Seed.demonlist-seed.json";

    private static readonly JsonSerializerOptions SeedJson = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Заполняет список, если он пуст. Возвращает число добавленных позиций
    /// (ноль — список уже был заполнен).
    /// </summary>
    public static async Task<int> SeedAsync(ApiDbContext db, CancellationToken ct = default)
    {
        if (await db.Demons.AnyAsync(ct))
            return 0;

        var entries = LoadSeed();
        if (entries.Count == 0)
            return 0;

        var now = DateTime.UtcNow;
        db.Demons.AddRange(entries.Select(e => new DemonListEntry
        {
            Position = e.Position,
            Name = e.Name,
            Publisher = e.Publisher ?? string.Empty,
            Verifier = e.Verifier ?? string.Empty,
            LevelId = e.LevelId,
            Video = e.Video,
            Thumbnail = e.Thumbnail,
            Requirement = e.Requirement ?? 100,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        }));

        await db.SaveChangesAsync(ct);
        return entries.Count;
    }

    private static IReadOnlyList<DemonSeedEntry> LoadSeed()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
        if (stream is null)
            return [];

        var file = JsonSerializer.Deserialize<DemonSeedFile>(stream, SeedJson);
        if (file?.Entries is not { Count: > 0 } entries)
            return [];

        // Места нумеруются подряд от единицы независимо от того, что лежало
        // в файле: дальше весь код перестановок опирается на эту непрерывность.
        return entries
            .OrderBy(e => e.Position)
            .Select((e, index) => e with { Position = index + 1 })
            .ToList();
    }
}
