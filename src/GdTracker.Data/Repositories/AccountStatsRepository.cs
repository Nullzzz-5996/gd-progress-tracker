using System.Text.Json;
using GdTracker.Core.Abstractions;
using GdTracker.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace GdTracker.Data.Repositories;

/// <inheritdoc />
public class AccountStatsRepository : IAccountStatsRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public AccountStatsRepository(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    public async Task<AccountStatsSnapshot?> GetLatestAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.AccountStatsSnapshots.AsNoTracking()
            .OrderByDescending(s => s.CapturedAt)
            .ThenByDescending(s => s.Id)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<AccountStatsSnapshot> AddIfChangedAsync(
        AccountStats stats, DateTime? saveFileWrittenAt, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var latest = await db.AccountStatsSnapshots
            .OrderByDescending(s => s.CapturedAt)
            .ThenByDescending(s => s.Id)
            .FirstOrDefaultAsync(ct);

        if (latest is not null && !HasChanges(latest, stats))
        {
            // Время записи файла само по себе новую строку не создаёт: игра переписывает
            // сейв при каждом выходе, даже если ни один счётчик не изменился.
            if (saveFileWrittenAt is not null && latest.SaveFileWrittenAt != saveFileWrittenAt)
            {
                latest.SaveFileWrittenAt = saveFileWrittenAt;
                await db.SaveChangesAsync(ct);
            }

            return latest;
        }

        var snapshot = new AccountStatsSnapshot
        {
            CapturedAt = DateTime.UtcNow,
            SaveFileWrittenAt = saveFileWrittenAt,
            Stars = stats.Stars,
            Moons = stats.Moons,
            Demons = stats.Demons,
            OnlineLevelsCompleted = stats.OnlineLevelsCompleted,
            OfficialLevelsCompleted = stats.OfficialLevelsCompleted,
            SecretCoins = stats.SecretCoins,
            Attempts = stats.Attempts,
            Jumps = stats.Jumps,
            TotalOrbs = stats.TotalOrbs,
            RawValuesJson = JsonSerializer.Serialize(stats.RawValues),
        };

        db.AccountStatsSnapshots.Add(snapshot);
        await db.SaveChangesAsync(ct);
        return snapshot;
    }

    public async Task<IReadOnlyList<AccountStatsSnapshot>> GetHistoryAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.AccountStatsSnapshots.AsNoTracking()
            .OrderBy(s => s.CapturedAt)
            .ThenBy(s => s.Id)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Сравнение только по девяти метрикам, не по RawValuesJson: посторонний ключ,
    /// меняющийся при каждом запуске игры, иначе плодил бы строки на пустом месте.
    /// </summary>
    private static bool HasChanges(AccountStatsSnapshot latest, AccountStats stats)
        => latest.Stars != stats.Stars
           || latest.Moons != stats.Moons
           || latest.Demons != stats.Demons
           || latest.OnlineLevelsCompleted != stats.OnlineLevelsCompleted
           || latest.OfficialLevelsCompleted != stats.OfficialLevelsCompleted
           || latest.SecretCoins != stats.SecretCoins
           || latest.Attempts != stats.Attempts
           || latest.Jumps != stats.Jumps
           || latest.TotalOrbs != stats.TotalOrbs;
}
