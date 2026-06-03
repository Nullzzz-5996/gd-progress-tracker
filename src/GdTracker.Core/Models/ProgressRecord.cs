namespace GdTracker.Core.Models;

/// <summary>
/// Одно событие прогресса на уровне. Покрывает оба типа:
/// «с нуля» (<see cref="RunType.FromZero"/>, <see cref="StartPercent"/> = 0)
/// и сегмент «с X% до Y%» (<see cref="RunType.Segment"/>).
/// </summary>
public class ProgressRecord
{
    /// <summary>Внутренний первичный ключ.</summary>
    public int Id { get; set; }

    /// <summary>Уровень, к которому относится запись.</summary>
    public int LevelId { get; set; }

    /// <summary>Навигационное свойство к уровню.</summary>
    public Level? Level { get; set; }

    /// <summary>Тип записи (с нуля / сегмент).</summary>
    public RunType Type { get; set; }

    /// <summary>Стартовый процент. Для <see cref="RunType.FromZero"/> равен 0.</summary>
    public int StartPercent { get; set; }

    /// <summary>Достигнутый процент (0–100).</summary>
    public int ReachedPercent { get; set; }

    /// <summary>Режим прохождения (обычный / практика).</summary>
    public ProgressMode Mode { get; set; }

    /// <summary>Сколько попыток заняло достижение (необязательно).</summary>
    public int? Attempts { get; set; }

    /// <summary>Дата заходa.</summary>
    public DateTime Date { get; set; }

    /// <summary>Произвольная заметка пользователя.</summary>
    public string? Note { get; set; }

    /// <summary>Источник записи (ручной / импорт сейва / live).</summary>
    public ProgressSource Source { get; set; } = ProgressSource.Manual;

    /// <summary>Прикреплённые видеоклипы (Фаза 4).</summary>
    public List<VideoClip> Videos { get; set; } = new();
}
