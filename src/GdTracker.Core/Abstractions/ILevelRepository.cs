using GdTracker.Core.Models;

namespace GdTracker.Core.Abstractions;

/// <summary>Хранилище уровней.</summary>
public interface ILevelRepository
{
    Task<IReadOnlyList<Level>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Уровень вместе с его записями прогресса; null если не найден.</summary>
    Task<Level?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>Уровень по GD Level ID (для дедупликации при добавлении из онлайн-поиска); null если нет.</summary>
    Task<Level?> GetByGdLevelIdAsync(long gdLevelId, CancellationToken ct = default);

    Task<Level> AddAsync(Level level, CancellationToken ct = default);

    Task UpdateAsync(Level level, CancellationToken ct = default);

    Task DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>Удаляет несколько уровней по их внутренним Id (групповое удаление).</summary>
    Task DeleteManyAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default);
}
