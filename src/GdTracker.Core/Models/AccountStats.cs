namespace GdTracker.Core.Models;

/// <summary>
/// Счётчики аккаунта Geometry Dash из блока GS_value сейв-файла.
/// Значения накопительные: они не уменьшаются, когда уровень исчезает из локальных записей,
/// поэтому агрегацией таблицы уровней их получить нельзя.
/// </summary>
public sealed record AccountStats
{
    /// <summary>Звёзды (ключ 6).</summary>
    public long Stars { get; init; }

    /// <summary>Луны — валюта платформерных уровней 2.2 (ключ 28).</summary>
    public long Moons { get; init; }

    /// <summary>Пройдено демонов (ключ 5).</summary>
    public long Demons { get; init; }

    /// <summary>Пройдено онлайн-уровней (ключ 4).</summary>
    public long OnlineLevelsCompleted { get; init; }

    /// <summary>Пройдено официальных уровней (ключ 3).</summary>
    public long OfficialLevelsCompleted { get; init; }

    /// <summary>Секретные монеты (ключ 8). Не путать с пользовательскими монетами (ключ 12).</summary>
    public long SecretCoins { get; init; }

    /// <summary>Попыток за всё время (ключ 2).</summary>
    public long Attempts { get; init; }

    /// <summary>Прыжков за всё время (ключ 1).</summary>
    public long Jumps { get; init; }

    /// <summary>Сфер собрано за всё время (ключ 22). Не путать с текущим балансом (ключ 14).</summary>
    public long TotalOrbs { get; init; }

    /// <summary>
    /// Все числовые ключи GS_value как есть — архив на будущее.
    /// Историю по метрикам вне девятки иначе не восстановить задним числом.
    /// </summary>
    public IReadOnlyDictionary<string, long> RawValues { get; init; }
        = new Dictionary<string, long>();
}
