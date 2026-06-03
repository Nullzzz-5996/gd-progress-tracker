namespace GdTracker.Core.Models;

/// <summary>
/// Уровень Geometry Dash, прогресс по которому отслеживается.
/// Агрегаты (лучшие проценты, попытки, завершённость) пересчитываются
/// из связанных <see cref="ProgressRecord"/> доменным сервисом.
/// </summary>
public class Level
{
    /// <summary>Внутренний первичный ключ.</summary>
    public int Id { get; set; }

    /// <summary>ID уровня в Geometry Dash; null для чисто ручного уровня.</summary>
    public long? GdLevelId { get; set; }

    /// <summary>Название уровня.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Происхождение уровня.</summary>
    public LevelSource Source { get; set; } = LevelSource.Custom;

    /// <summary>Сколько звёзд стоит уровень (k26 из сейва), если известно.</summary>
    public int? Stars { get; set; }

    /// <summary>Лучший достигнутый процент в обычном режиме (0–100).</summary>
    public int BestNormalPercent { get; set; }

    /// <summary>Лучший достигнутый процент в режиме практики (0–100).</summary>
    public int BestPracticePercent { get; set; }

    /// <summary>Суммарное количество попыток.</summary>
    public int TotalAttempts { get; set; }

    /// <summary>Уровень пройден (лучший normal % == 100).</summary>
    public bool IsCompleted { get; set; }

    /// <summary>Дата добавления уровня.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Дата последнего обновления.</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>Записи прогресса по этому уровню.</summary>
    public List<ProgressRecord> ProgressRecords { get; set; } = new();
}
