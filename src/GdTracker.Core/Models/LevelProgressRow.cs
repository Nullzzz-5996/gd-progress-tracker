namespace GdTracker.Core.Models;

/// <summary>
/// Одна строка личной таблицы прогресса на вкладке «Прогрессы».
/// Столбцы 1–3 заполняются вручную; столбец «с нуля» вычисляется в UI из
/// <see cref="Level.BestNormalPercent"/> и здесь не хранится.
/// </summary>
public class LevelProgressRow
{
    /// <summary>Внутренний первичный ключ.</summary>
    public int Id { get; set; }

    /// <summary>Уровень, к которому относится строка.</summary>
    public int LevelId { get; set; }

    /// <summary>Навигационное свойство к уровню.</summary>
    public Level? Level { get; set; }

    /// <summary>Позиция строки в таблице уровня (1…100).</summary>
    public int Position { get; set; }

    /// <summary>Столбец 1 «практика — за сколько попыток» (например «23»).</summary>
    public string? PracticeAttempts { get; set; }

    /// <summary>Столбец 2 «с парта до парта» (например «24-35»).</summary>
    public string? SegmentRange { get; set; }

    /// <summary>Столбец 3 «до ста» (например «67-100»).</summary>
    public string? ToHundredRange { get; set; }

    /// <summary>Дата создания строки.</summary>
    public DateTime CreatedAt { get; set; }
}
