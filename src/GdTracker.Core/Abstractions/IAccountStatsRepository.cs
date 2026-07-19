using GdTracker.Core.Models;

namespace GdTracker.Core.Abstractions;

/// <summary>Хранилище снимков статистики аккаунта.</summary>
public interface IAccountStatsRepository
{
    /// <summary>Последний снимок по времени съёмки, либо null, если снимков ещё нет.</summary>
    Task<AccountStatsSnapshot?> GetLatestAsync(CancellationToken ct = default);

    /// <summary>
    /// Сохраняет новый снимок, если хоть одна из девяти метрик отличается от последнего.
    /// Если метрики не изменились, но передано значение <paramref name="saveFileWrittenAt"/>,
    /// обновляет это поле у существующего снимка в БД.
    /// В любом случае возвращает актуальный снимок — новый или существующий.
    /// </summary>
    Task<AccountStatsSnapshot> AddIfChangedAsync(
        AccountStats stats, DateTime? saveFileWrittenAt, CancellationToken ct = default);

    /// <summary>
    /// Все снимки от старых к новым — для графика динамики.
    /// Ограничения по количеству нет: строка пишется только при изменении метрик, их мало.
    /// </summary>
    Task<IReadOnlyList<AccountStatsSnapshot>> GetHistoryAsync(CancellationToken ct = default);
}
