using GdTracker.Core.Models;

namespace GdTracker.Core.Abstractions;

/// <summary>Хранилище строк личной таблицы прогресса (вкладка «Прогрессы»).</summary>
public interface ILevelProgressRowRepository
{
    Task<IReadOnlyList<LevelProgressRow>> GetByLevelAsync(int levelId, CancellationToken ct = default);
    Task<LevelProgressRow> AddAsync(LevelProgressRow row, CancellationToken ct = default);
    Task UpdateAsync(LevelProgressRow row, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}
