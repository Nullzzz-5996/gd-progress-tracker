namespace GdTracker.Core.Models;

/// <summary>
/// Снимок счётчиков аккаунта на момент чтения сейва.
/// Копится, чтобы позже построить динамику: сам сейв истории не хранит.
/// </summary>
public class AccountStatsSnapshot
{
    public int Id { get; set; }

    /// <summary>Когда снимок сделан (UTC).</summary>
    public DateTime CapturedAt { get; set; }

    /// <summary>Время последней записи сейв-файла на момент снимка (UTC).</summary>
    public DateTime? SaveFileWrittenAt { get; set; }

    public long Stars { get; set; }
    public long Moons { get; set; }
    public long Demons { get; set; }
    public long OnlineLevelsCompleted { get; set; }
    public long OfficialLevelsCompleted { get; set; }
    public long SecretCoins { get; set; }
    public long Attempts { get; set; }
    public long Jumps { get; set; }
    public long TotalOrbs { get; set; }

    /// <summary>
    /// Все числовые ключи GS_value в виде JSON — архив на будущее.
    /// Единственное, что нельзя добавить задним числом: без него история
    /// по метрикам вне девятки начнётся только с момента будущей доработки.
    /// </summary>
    public string RawValuesJson { get; set; } = "{}";
}
