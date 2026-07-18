using GdTracker.Core.Models;

namespace GdTracker.Core.Abstractions;

/// <summary>Хранилище снимков статистики аккаунта.</summary>
public interface IAccountStatsRepository
{
    /// <summary>Последний снимок по времени съёмки, либо null, если снимков ещё нет.</summary>
    Task<AccountStatsSnapshot?> GetLatestAsync(CancellationToken ct = default);

    /// <summary>
    /// Сохраняет снимок, если хоть одна метрика отличается от последнего.
    /// В любом случае возвращает актуальный снимок — новый или существующий.
    /// </summary>
    Task<AccountStatsSnapshot> AddIfChangedAsync(
        AccountStats stats, DateTime? saveFileWrittenAt, CancellationToken ct = default);
}
