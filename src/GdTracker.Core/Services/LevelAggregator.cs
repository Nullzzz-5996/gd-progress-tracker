using GdTracker.Core.Models;

namespace GdTracker.Core.Services;

/// <summary>Агрегаты уровня, вычисляемые из записей прогресса.</summary>
public readonly record struct LevelAggregates(
    int BestNormalPercent,
    int BestPracticePercent,
    int TotalAttempts,
    bool IsCompleted);

/// <summary>
/// Пересчёт агрегатов уровня (лучшие проценты, суммарные попытки, завершённость)
/// из связанных <see cref="ProgressRecord"/>.
/// </summary>
public static class LevelAggregator
{
    /// <summary>Вычисляет агрегаты из набора записей прогресса.</summary>
    public static LevelAggregates Compute(IEnumerable<ProgressRecord> records)
    {
        int bestNormal = 0;
        int bestPractice = 0;
        int totalAttempts = 0;

        foreach (var r in records)
        {
            if (r.Mode == ProgressMode.Normal)
                bestNormal = Math.Max(bestNormal, r.ReachedPercent);
            else
                bestPractice = Math.Max(bestPractice, r.ReachedPercent);

            totalAttempts += r.Attempts ?? 0;
        }

        return new LevelAggregates(bestNormal, bestPractice, totalAttempts, bestNormal >= 100);
    }

    /// <summary>Пересчитывает и применяет агрегаты к уровню по его записям прогресса.</summary>
    public static void Apply(Level level)
    {
        var aggregates = Compute(level.ProgressRecords);
        level.BestNormalPercent = aggregates.BestNormalPercent;
        level.BestPracticePercent = aggregates.BestPracticePercent;
        level.TotalAttempts = aggregates.TotalAttempts;
        level.IsCompleted = aggregates.IsCompleted;
    }
}
