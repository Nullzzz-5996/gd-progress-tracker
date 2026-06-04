using GdTracker.Core.Models;

namespace GdTracker.Core.Abstractions;

/// <summary>Результат импорта сейва.</summary>
public readonly record struct SaveImportResult(int LevelsAdded, int LevelsUpdated)
{
    public int Total => LevelsAdded + LevelsUpdated;
}

/// <summary>
/// Импорт прочитанных из сейва уровней в БД: создание новых и обновление
/// существующих (идемпотентно — повторный импорт не дублирует данные).
/// </summary>
public interface ISaveImportService
{
    Task<SaveImportResult> ImportAsync(IReadOnlyList<SaveLevelDto> levels, CancellationToken ct = default);
}
