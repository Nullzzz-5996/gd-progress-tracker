using GdTracker.Core.Models;

namespace GdTracker.Core.Services;

/// <summary>Группа распределения уровней по лучшему normal-проценту.</summary>
public readonly record struct PercentBucket(string Label, int Count);

/// <summary>Уровень и число его попыток (для топа).</summary>
public readonly record struct LevelAttempts(string Name, int Attempts);

/// <summary>Сводная статистика по всем уровням.</summary>
public sealed record ProgressStatistics
{
    public int TotalLevels { get; init; }
    public int Completed { get; init; }
    public int InProgress { get; init; }
    public int Untouched { get; init; }
    public int TotalAttempts { get; init; }
    public int OfficialCompleted { get; init; }
    public int OfficialTotal { get; init; }
    public IReadOnlyList<PercentBucket> NormalPercentBuckets { get; init; } = [];
    public IReadOnlyList<LevelAttempts> TopByAttempts { get; init; } = [];
}

/// <summary>Вычисляет сводную статистику по набору уровней (чистая функция).</summary>
public static class StatisticsCalculator
{
    private static readonly string[] BucketLabels =
        ["0%", "1–25%", "26–50%", "51–75%", "76–99%", "100%"];

    public static ProgressStatistics Compute(IReadOnlyList<Level> levels, int topCount = 10)
    {
        var counts = new int[BucketLabels.Length];
        foreach (var l in levels)
            counts[BucketIndex(l.BestNormalPercent)]++;

        return new ProgressStatistics
        {
            TotalLevels = levels.Count,
            Completed = levels.Count(l => l.IsCompleted),
            Untouched = levels.Count(l => l.BestNormalPercent == 0 && !l.IsCompleted),
            InProgress = levels.Count(l => l.BestNormalPercent is > 0 and < 100 && !l.IsCompleted),
            TotalAttempts = levels.Sum(l => l.TotalAttempts),
            OfficialTotal = levels.Count(l => l.Source == LevelSource.Official),
            OfficialCompleted = levels.Count(l => l.Source == LevelSource.Official && l.IsCompleted),
            NormalPercentBuckets = BucketLabels
                .Select((label, i) => new PercentBucket(label, counts[i]))
                .ToList(),
            TopByAttempts = levels
                .OrderByDescending(l => l.TotalAttempts)
                .Take(topCount)
                .Select(l => new LevelAttempts(l.Name, l.TotalAttempts))
                .ToList(),
        };
    }

    private static int BucketIndex(int percent) => percent switch
    {
        <= 0 => 0,
        <= 25 => 1,
        <= 50 => 2,
        <= 75 => 3,
        < 100 => 4,
        _ => 5,
    };
}
