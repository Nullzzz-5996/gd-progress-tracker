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

            output.Add(new SaveLevelDto
            {
                GdLevelId = id,
                Name = GetString(map, "k2"),
                Source = source,
                BestNormalPercent = (int)(GetLong(map, "k19") ?? 0),
                BestPracticePercent = (int)(GetLong(map, "k20") ?? 0),
                Attempts = (int)(GetLong(map, "k18") ?? 0),
                Stars = GetLong(map, "k26") is { } stars ? (int)stars : null,
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

    private static string? GetString(Dictionary<string, XElement> map, string key)
        => map.TryGetValue(key, out var el) ? el.Value : null;
}
