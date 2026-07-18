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
            var tag = xml.Substring(lt + 1, gt - lt - 1).Trim();
            var selfClosing = tag.EndsWith('/');
            var closing = tag.StartsWith('/');
            var name = tag.Trim('/', ' ');

            if (!selfClosing && name is "d" or "dict")
            {
                if (closing)
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
