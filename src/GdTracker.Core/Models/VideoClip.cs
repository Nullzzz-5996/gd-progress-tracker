namespace GdTracker.Core.Models;

/// <summary>
/// Видеоматериал, привязанный к уровню и/или записи прогресса.
/// Может быть локальным файлом (скопированным в app-data) либо внешней ссылкой (YouTube).
/// Полноценно используется с Фазы 4; таблица создаётся сразу в первой миграции.
/// </summary>
public class VideoClip
{
    /// <summary>Внутренний первичный ключ.</summary>
    public int Id { get; set; }

    /// <summary>Уровень-владелец (необязательно).</summary>
    public int? LevelId { get; set; }

    /// <summary>Запись прогресса-владелец (необязательно).</summary>
    public int? ProgressRecordId { get; set; }

    /// <summary>true — это внешняя ссылка (<see cref="Url"/>); false — локальный файл (<see cref="LocalPath"/>).</summary>
    public bool IsExternalUrl { get; set; }

    /// <summary>URL внешнего видео (например, YouTube).</summary>
    public string? Url { get; set; }

    /// <summary>Путь к локальной копии файла (%LOCALAPPDATA%\GdTracker\media\&lt;id&gt;.mp4).</summary>
    public string? LocalPath { get; set; }

    /// <summary>Исходное имя файла.</summary>
    public string? OriginalName { get; set; }

    /// <summary>Длительность в секундах, если определена.</summary>
    public double? DurationSeconds { get; set; }

    /// <summary>Путь к сгенерированному превью.</summary>
    public string? ThumbnailPath { get; set; }

    /// <summary>Дата добавления.</summary>
    public DateTime CreatedAt { get; set; }
}
