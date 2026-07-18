using GdTracker.Core.Models;

namespace GdTracker.Core.Abstractions;

/// <summary>Чтение прогресса уровней из сейв-файла Geometry Dash.</summary>
public interface ISaveFileReader
{
    /// <summary>Путь к сейв-файлу по умолчанию, если он найден; иначе null.</summary>
    string? DefaultSaveFilePath { get; }

    /// <summary>Декодирует и парсит сейв-файл, возвращая уровни с прогрессом.</summary>
    IReadOnlyList<SaveLevelDto> ReadLevels(string saveFilePath);

    /// <summary>
    /// Декодирует сейв и возвращает счётчики аккаунта.
    /// Возвращает null, если блока GS_value в файле нет.
    /// </summary>
    AccountStats? ReadAccountStats(string saveFilePath);

    /// <summary>Время последней записи сейв-файла (UTC), либо null, если файла нет.</summary>
    DateTime? GetLastWriteTimeUtc(string saveFilePath);
}
