using FluentAssertions;
using GdTracker.Core;
using GdTracker.Core.Models;
using GdTracker.Core.Services;

namespace GdTracker.Tests;

public class LevelAggregatorTests
{
    private static ProgressRecord Rec(ProgressMode mode, int reached, int start = 0, int? attempts = null)
        => new()
        {
            Mode = mode,
            ReachedPercent = reached,
            StartPercent = start,
            Attempts = attempts,
            Type = start == 0 ? RunType.FromZero : RunType.Segment,
        };

    [Fact]
    public void Empty_records_produce_zero_aggregates()
    {
        LevelAggregator.Compute(Array.Empty<ProgressRecord>())
            .Should().Be(new LevelAggregates(0, 0, 0, false));
    }

    [Fact]
    public void Best_normal_is_max_reached_among_normal_records()
    {
        var a = LevelAggregator.Compute(new[] { Rec(ProgressMode.Normal, 45), Rec(ProgressMode.Normal, 80) });
        a.BestNormalPercent.Should().Be(80);
    }

    [Fact]
    public void Practice_and_normal_are_tracked_separately()
    {
        var a = LevelAggregator.Compute(new[] { Rec(ProgressMode.Normal, 45), Rec(ProgressMode.Practice, 60, 30) });
        a.BestNormalPercent.Should().Be(45);
        a.BestPracticePercent.Should().Be(60);
    }

    [Fact]
    public void Total_attempts_sums_non_null_attempts()
    {
        var a = LevelAggregator.Compute(new[]
        {
            Rec(ProgressMode.Normal, 45, attempts: 10),
            Rec(ProgressMode.Normal, 80, attempts: 25),
            Rec(ProgressMode.Practice, 60, 30, attempts: null),
        });
        a.TotalAttempts.Should().Be(35);
    }

    [Fact]
    public void Normal_100_marks_completed()
    {
        LevelAggregator.Compute(new[] { Rec(ProgressMode.Normal, 100) }).IsCompleted.Should().BeTrue();
    }

    [Fact]
    public void Practice_100_without_normal_100_is_not_completed()
    {
        var a = LevelAggregator.Compute(new[] { Rec(ProgressMode.Practice, 100, 30), Rec(ProgressMode.Normal, 90) });
        a.IsCompleted.Should().BeFalse();
        a.BestPracticePercent.Should().Be(100);
    }

    [Fact]
    public void Apply_updates_level_aggregate_fields_from_records()
    {
        var level = new Level();
        level.ProgressRecords.Add(Rec(ProgressMode.Normal, 45, attempts: 10));
        level.ProgressRecords.Add(Rec(ProgressMode.Practice, 60, 30, attempts: 5));

        LevelAggregator.Apply(level);

        level.BestNormalPercent.Should().Be(45);
        level.BestPracticePercent.Should().Be(60);
        level.TotalAttempts.Should().Be(15);
        level.IsCompleted.Should().BeFalse();
    }
}
