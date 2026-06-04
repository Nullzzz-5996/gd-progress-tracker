namespace GdTracker.Core.Abstractions;

/// <summary>Итог импорта пакета прогресса.</summary>
public readonly record struct ImportSummary(int LevelsAdded, int LevelsUpdated, int RecordsAdded);

/// <summary>Экспорт/импорт прогресса в файл для обмена с другими пользователями.</summary>
public interface IProgressSharingService
{
    /// <summary>Экспортирует весь прогресс в JSON-файл.</summary>
    Task ExportAsync(string filePath, CancellationToken ct = default);

    /// <summary>Импортирует прогресс из JSON-файла (слияние, без дублирования записей).</summary>
    Task<ImportSummary> ImportAsync(string filePath, CancellationToken ct = default);
}
