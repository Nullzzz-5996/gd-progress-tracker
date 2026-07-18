using System.Xml.Linq;
using GdTracker.Core.Models;

namespace GdTracker.GameSync;

/// <summary>
/// Извлекает счётчики аккаунта из блока GS_value декодированного plist сейва GD.
/// Расшифровка ключей — реверс-инжиниринг сообщества (GD Docs и линейка gd.py/GDColon),
/// проверенная на реальном сейве: ключ 3 сходится точно с числом официальных уровней на 100%.
/// Показываются только ключи, по которым источники не расходятся.
/// </summary>
public static class AccountStatsParser
{
    private const string GsValueKey = "<k>GS_value</k>";

    // Номера ключей внутри GS_value.
    private const string KeyJumps = "1";
    private const string KeyAttempts = "2";
    private const string KeyOfficialLevels = "3";
    private const string KeyOnlineLevels = "4";
    private const string KeyDemons = "5";
    private const string KeyStars = "6";
    private const string KeySecretCoins = "8";   // 12 — пользовательские монеты, это другое
    private const string KeyTotalOrbs = "22";    // 14 — текущий баланс, это другое
    private const string KeyMoons = "28";

    /// <summary>
    /// Разбирает GS_value. Возвращает null, если блока в файле нет
    /// (иначе нельзя отличить «статистики нет» от «всё по нулям»).
    /// </summary>
    public static AccountStats? Parse(string plistXml)
    {
        var fragment = ExtractGsValueFragment(plistXml);
        if (fragment is null)
            return null;

        var values = ReadNumericEntries(XDocument.Parse(fragment).Root!);

        return new AccountStats
        {
            Jumps = Get(values, KeyJumps),
            Attempts = Get(values, KeyAttempts),
            OfficialLevelsCompleted = Get(values, KeyOfficialLevels),
            OnlineLevelsCompleted = Get(values, KeyOnlineLevels),
            Demons = Get(values, KeyDemons),
            Stars = Get(values, KeyStars),
            SecretCoins = Get(values, KeySecretCoins),
            TotalOrbs = Get(values, KeyTotalOrbs),
            Moons = Get(values, KeyMoons),
            RawValues = values,
        };
    }

    /// <summary>
    /// Вырезает словарь-значение ключа GS_value как самостоятельный XML-фрагмент.
    /// Разбирать документ целиком нельзя по цене: сейв распаковывается в ~56 млн символов,
    /// а нужный блок — около 3 КБ в самом его конце.
    /// </summary>
    private static string? ExtractGsValueFragment(string xml)
    {
        var keyIdx = xml.IndexOf(GsValueKey, StringComparison.Ordinal);
        if (keyIdx < 0)
            return null;

        var start = xml.IndexOf('<', keyIdx + GsValueKey.Length);
        if (start < 0)
            return null;

        // Проверяем, что значение GS_value — действительно словарь.
        // Если это не открывающий/самозакрывающийся <d> или <dict>, возвращаем null,
        // не сканируя дальше. Классификация тега — общая с основным циклом ниже
        // (см. ClassifyTag), чтобы предпроверка и скан не могли разойтись в том,
        // что считается открывающим, а что закрывающим тегом.
        var firstGt = xml.IndexOf('>', start);
        if (firstGt < 0)
            return null;

        var firstTag = ClassifyTag(xml.Substring(start + 1, firstGt - start - 1));
        if (firstTag.Name is not "d" and not "dict" || firstTag.IsClosing)
            return null;

        var depth = 0;
        var i = start;
        while (i < xml.Length)
        {
            var lt = xml.IndexOf('<', i);
            if (lt < 0)
                break;

            var gt = xml.IndexOf('>', lt);
            if (gt < 0)
                break;

            // Сканируется только блок GS_value (~3 КБ), не весь документ:
            // поиск по 56 млн символов уже позади, в IndexOf выше.
            var tag = ClassifyTag(xml.Substring(lt + 1, gt - lt - 1));

            if (tag.Name is "d" or "dict")
            {
                if (tag.IsSelfClosing)
                {
                    // <d/> — открытие и закрытие одним тегом, суммарная глубина не меняется.
                    // Если это и есть значение GS_value (глубина ещё 0), фрагмент — сам этот тег.
                    if (depth == 0)
                        return xml.Substring(start, gt - start + 1);
                }
                else if (tag.IsClosing)
                {
                    depth--;
                    if (depth == 0)
                        return xml.Substring(start, gt - start + 1);
                }
                else
                {
                    depth++;
                }
            }

            i = gt + 1;
        }

        return null;
    }

    /// <summary>
    /// Разбирает содержимое одного тега (то, что между «&lt;» и «&gt;», без самих скобок)
    /// и определяет его имя и вид — открывающий, закрывающий или самозакрывающийся.
    /// Единственное место, где принимается это решение: используется и в предпроверке
    /// первого тега значения GS_value, и в основном скане глубины, — раньше у них были
    /// свои, чуть разные копии этой логики, и расхождение между ними породило баг
    /// (закрывающий тег «/dict» после Trim('/', ' ') давал то же имя «dict», что и
    /// открывающий, и ошибочно принимался за валидное значение).
    /// </summary>
    private static TagInfo ClassifyTag(string tagContent)
    {
        var trimmed = tagContent.Trim();
        var isSelfClosing = trimmed.EndsWith('/');
        var isClosing = trimmed.StartsWith('/');
        var name = trimmed.Trim('/', ' ');
        var isOpening = !isSelfClosing && !isClosing;

        return new TagInfo(name, isOpening, isClosing, isSelfClosing);
    }

    /// <summary>Имя тега и его вид: открывающий, закрывающий или самозакрывающийся.</summary>
    private readonly record struct TagInfo(string Name, bool IsOpening, bool IsClosing, bool IsSelfClosing);

    /// <summary>
    /// Собирает пары «числовой ключ → значение».
    /// В GS_value рядом со статистикой лежат ~77 записей unique_&lt;levelID&gt;_&lt;coinIdx&gt;
    /// (флаги собранных монет официальных уровней) — они отбрасываются.
    /// </summary>
    private static Dictionary<string, long> ReadNumericEntries(XElement dict)
    {
        var values = new Dictionary<string, long>();
        string? key = null;

        foreach (var el in dict.Elements())
        {
            if (el.Name.LocalName == "k")
            {
                key = el.Value;
                continue;
            }

            if (key is not null && IsNumericKey(key) && long.TryParse(el.Value, out var v))
                values[key] = v;

            key = null;
        }

        return values;
    }

    private static bool IsNumericKey(string key)
        => key.Length > 0 && key.All(char.IsAsciiDigit);

    private static long Get(Dictionary<string, long> values, string key)
        => values.TryGetValue(key, out var v) ? v : 0;
}
