using FluentAssertions;
using GdTracker.Core;
using GdTracker.Core.Models;
using GdTracker.Core.Services;

namespace GdTracker.Tests;

public class StatisticsCalculatorTests
{
    private static Level L(LevelSource src, int normal, int attempts, string name = "x") => new()
    {
        Name = name,
        Source = src,
        BestNormalPercent = normal,
        TotalAttempts = attempts,
        IsCompleted = normal == 100,
    };

    private static readonly List<Level> Sample =
    [
        L(LevelSource.Official, 100, 30, "Stereo Madness"),
        L(LevelSource.Official, 0, 5, "Deadlocked"),
        L(LevelSource.Online, 45, 100, "MidLevel"),
        L(LevelSource.Online, 100, 2180, "Bloodbath"),
        L(LevelSource.Online, 0, 0, "Untouched"),
        L(LevelSource.Online, 80, 500, "AlmostThere"),
    ];

    [Fact]
    public void Computes_completion_counts()
    {
        var s = StatisticsCalculator.Compute(Sample);
        s.TotalLevels.Should().Be(6);
        s.Completed.Should().Be(2);
        s.Untouched.Should().Be(2);
        s.InProgress.Should().Be(2);
    }

    [Fact]
    public void Sums_attempts_and_official_completion()
    {
        var s = StatisticsCalculator.Compute(Sample);
        s.TotalAttempts.Should().Be(2815);
        s.OfficialTotal.Should().Be(2);
        s.OfficialCompleted.Should().Be(1);
    }

    [Fact]
    public void Buckets_distribute_by_best_normal_percent()
    {
        var s = StatisticsCalculator.Compute(Sample);
        s.NormalPercentBuckets.Should().HaveCount(6);
        s.NormalPercentBuckets[0].Should().Be(new PercentBucket("0%", 2));
        s.NormalPercentBuckets[2].Should().Be(new PercentBucket("26–50%", 1)); // 45
        s.NormalPercentBuckets[4].Should().Be(new PercentBucket("76–99%", 1)); // 80
        s.NormalPercentBuckets[5].Should().Be(new PercentBucket("100%", 2));
    }

    [Fact]
    public void Top_by_attempts_is_sorted_desc_and_limited()
    {
        var s = StatisticsCalculator.Compute(Sample, topCount: 3);
        s.TopByAttempts.Should().HaveCount(3);
        s.TopByAttempts[0].Should().Be(new LevelAttempts("Bloodbath", 2180));
        s.TopByAttempts[1].Should().Be(new LevelAttempts("AlmostThere", 500));
        s.TopByAttempts[2].Should().Be(new LevelAttempts("MidLevel", 100));
    }

    [Fact]
    public void Empty_levels_produce_zeroed_stats()
    {
        var s = StatisticsCalculator.Compute([]);
        s.TotalLevels.Should().Be(0);
        s.NormalPercentBuckets.Should().HaveCount(6);
        s.TopByAttempts.Should().BeEmpty();
    }

    [Fact]
    public void Completion_categories_are_mutually_exclusive_and_sum_to_total()
    {
        var s = StatisticsCalculator.Compute(Sample);
        (s.Completed + s.InProgress + s.Untouched).Should().Be(s.TotalLevels);
    }

    /// <summary>
    /// Дефект: три условия категорий были независимыми, а не взаимоисключающими
    /// разбиением. Уровень со 100% прогресса, но IsCompleted = false (например,
    /// импортированный до того, как игра отметила его пройденным), не попадал ни
    /// в «пройдено» (IsCompleted == false), ни в «не начато» (percent != 0), ни в
    /// «в процессе» (условие требовало percent &lt; 100) — сумма категорий не
    /// сходилась с TotalLevels, и круговая диаграмма теряла уровень.
    /// </summary>
    [Fact]
    public void Level_with_100_percent_but_not_completed_counts_as_in_progress_not_lost()
    {
        var levels = new List<Level>
        {
            new() { Name = "Edge", Source = LevelSource.Custom, BestNormalPercent = 100, IsCompleted = false, TotalAttempts = 1 },
        };

        var s = StatisticsCalculator.Compute(levels);

        s.Completed.Should().Be(0);
        s.Untouched.Should().Be(0);
        s.InProgress.Should().Be(1);
        (s.Completed + s.InProgress + s.Untouched).Should().Be(s.TotalLevels);
    }
}
