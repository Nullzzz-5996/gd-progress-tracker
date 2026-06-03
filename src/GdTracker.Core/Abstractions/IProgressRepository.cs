using GdTracker.Core.Models;

namespace GdTracker.Core.Abstractions;

/// <summary>
/// Хранилище записей прогресса. При добавлении/удалении пересчитывает
/// агрегаты родительского уровня (лучшие %, попытки, завершённость).
/// </summary>
public interface IProgressRepository
{
    Task<ProgressRecord> AddAsync(ProgressRecord record, CancellationToken ct = default);

    Task DeleteAsync(int id, CancellationToken ct = default);
}
