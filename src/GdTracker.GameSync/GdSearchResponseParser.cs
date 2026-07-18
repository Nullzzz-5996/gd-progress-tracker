using GdTracker.Core.Models;

namespace GdTracker.GameSync;

/// <summary>
/// Парсер ответа getGJLevels (Boomlings). Формат: секции через '#', уровни через '|',
/// поля key:value через ':'. Имена создателей — в секции creators (userID:userName:accountID).
/// </summary>
public static class GdSearchResponseParser
{
    public static IReadOnlyList<OnlineLevel> Parse(string response)
    {
        if (string.IsNullOrWhiteSpace(response) || response.StartsWith("-1"))
            return [];

        var sections = response.Split('#');
        var levelsPart = sections.Length > 0 ? sections[0] : string.Empty;
        var creators = ParseCreators(sections.Length > 1 ? sections[1] : string.Empty);

        var result = new List<OnlineLevel>();
        if (string.IsNullOrEmpty(levelsPart))
            return result;

        foreach (var levelStr in levelsPart.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var map = ParseKeyValues(levelStr);
            if (!map.TryGetValue(1, out var idStr) || !long.TryParse(idStr, out var id))
                continue;

            var userId = map.TryGetValue(6, out var u) && long.TryParse(u, out var uid) ? uid : 0;

            result.Add(new OnlineLevel
            {
                Id = id,
                Name = map.GetValueOrDefault(2, string.Empty),
                Creator = creators.GetValueOrDefault(userId),
                Stars = GetInt(map, 18),
                Downloads = GetInt(map, 10),
                Likes = GetInt(map, 14),
                Difficulty = OnlineDifficulty.Resolve(
                    GetInt(map, 25) > 0, GetInt(map, 17) > 0, GetInt(map, 43), GetInt(map, 9)),
            });
        }

        return result;
    }

    private static Dictionary<int, string> ParseKeyValues(string level)
    {
        var parts = level.Split(':');
        var map = new Dictionary<int, string>();
        for (var i = 0; i + 1 < parts.Length; i += 2)
            if (int.TryParse(parts[i], out var key))
                map[key] = parts[i + 1];
        return map;
    }

    private static Dictionary<long, string> ParseCreators(string section)
    {
        var map = new Dictionary<long, string>();
        if (string.IsNullOrEmpty(section))
            return map;

        foreach (var entry in section.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = entry.Split(':');
            if (parts.Length >= 2 && long.TryParse(parts[0], out var uid))
                map[uid] = parts[1];
        }

        return map;
    }

    private static int GetInt(Dictionary<int, string> map, int key)
        => map.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : 0;
}
