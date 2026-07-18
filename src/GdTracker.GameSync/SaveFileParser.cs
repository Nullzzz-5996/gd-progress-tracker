using System.Xml.Linq;
using GdTracker.Core;
using GdTracker.Core.Models;

namespace GdTracker.GameSync;

/// <summary>
/// Парсит декодированный plist сейва GD и извлекает прогресс уровней из
/// GLM_01 (официальные) и GLM_03 (онлайн). Формат — компактный plist GD:
/// словари &lt;dict&gt;/&lt;d&gt;, ключи &lt;k&gt;, значения &lt;i&gt;/&lt;s&gt;/&lt;r&gt;/&lt;t /&gt;.
/// </summary>
public static class SaveFileParser
{
    /// <summary>Извлекает уровни из plist XML сейва.</summary>
    public static IReadOnlyList<SaveLevelDto> Parse(string plistXml)
    {
        var doc = XDocument.Parse(plistXml);
        var root = doc.Root?.Elements().FirstOrDefault(e => e.Name.LocalName is "dict" or "d");
        if (root is null)
            return [];

        var result = new List<SaveLevelDto>();
        ParseGlm(root, "GLM_01", LevelSource.Official, result);
        ParseGlm(root, "GLM_03", LevelSource.Online, result);
        return result;
    }

    private static void ParseGlm(XElement root, string glmKey, LevelSource source, List<SaveLevelDto> output)
    {
        var glm = GetValue(root, glmKey);
        if (glm is null || !IsDict(glm))
            return;

        foreach (var (idKey, levelEl) in Pairs(glm))
        {
            if (!IsDict(levelEl))
                continue;

            var map = ToMap(levelEl);
            var id = GetLong(map, "k1") ?? (long.TryParse(idKey, out var k) ? k : 0);

            // k26 у официальных уровней — это их порядковый номер, а не звёзды,
            // поэтому звёзды и сложность из сейва достоверны только для online-уровней.
            var isOnline = source == LevelSource.Online;
            var rawStars = GetLong(map, "k26");
            var validStars = isOnline && rawStars is >= 0 and <= 10;
            var stars = validStars ? (int)rawStars!.Value : 0;

            var creator = GetString(map, "k5");
            if (string.IsNullOrEmpty(creator) && source == LevelSource.Official)
                creator = "RobTop";

            var difficulty = isOnline
                ? SaveDifficulty.Resolve(
                    GetBool(map, "k33"), GetBool(map, "k25"), (int)(GetLong(map, "k76") ?? 0), stars)
                : null;

            output.Add(new SaveLevelDto
            {
                GdLevelId = id,
                Name = GetString(map, "k2"),
                Source = source,
                BestNormalPercent = (int)(GetLong(map, "k19") ?? 0),
                BestPracticePercent = (int)(GetLong(map, "k20") ?? 0),
                Attempts = (int)(GetLong(map, "k18") ?? 0),
                Stars = validStars ? stars : null,
                Creator = creator,
                Difficulty = difficulty,
            });
        }
    }

    private static bool IsDict(XElement e) => e.Name.LocalName is "dict" or "d";

    /// <summary>Итерирует пары «ключ → значение» словаря (&lt;k&gt; затем элемент-значение).</summary>
    private static IEnumerable<(string Key, XElement Value)> Pairs(XElement dict)
    {
        string? key = null;
        foreach (var el in dict.Elements())
        {
            if (el.Name.LocalName == "k")
            {
                key = el.Value;
            }
            else if (key is not null)
            {
                yield return (key, el);
                key = null;
            }
        }
    }

    private static XElement? GetValue(XElement dict, string key)
    {
        foreach (var (k, v) in Pairs(dict))
            if (k == key)
                return v;
        return null;
    }

    private static Dictionary<string, XElement> ToMap(XElement dict)
    {
        var map = new Dictionary<string, XElement>();
        foreach (var (k, v) in Pairs(dict))
            map[k] = v;
        return map;
    }

    private static long? GetLong(Dictionary<string, XElement> map, string key)
        => map.TryGetValue(key, out var el) && long.TryParse(el.Value, out var v) ? v : null;

    /// <summary>Булев ключ: в plist GD true = &lt;t /&gt;, false опускается.</summary>
    private static bool GetBool(Dictionary<string, XElement> map, string key)
        => map.TryGetValue(key, out var el)
           && el.Name.LocalName != "f"
           && (el.Name.LocalName == "t" || el.Value is "1" or "true");

    private static string? GetString(Dictionary<string, XElement> map, string key)
        => map.TryGetValue(key, out var el) ? el.Value : null;
}
