using GdTracker.Core.Models;

namespace GdTracker.Core.Abstractions;

/// <summary>Хранилище уровней.</summary>
public interface ILevelRepository
{
    /// <summary>Все уровни, включая скрытые строки импорта из игры (нужно статистике).</summary>
    Task<IReadOnlyList<Level>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Уровни, видимые пользователю: без скрытых строк импорта из игры.</summary>
    Task<IReadOnlyList<Level>> GetTrackedAsync(CancellationToken ct = default);

    /// <summary>
    /// Скрытый уровень с таким же названием (без учёта регистра); при нескольких
    /// совпадениях — самый заигранный. null, если такого нет. Нужен ручному добавлению,
    /// чтобы подтянуть данные уровня, уже импортированного из игры.
    /// </summary>
    Task<Level?> FindUntrackedByNameAsync(string name, CancellationToken ct = default);

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
