using FluentAssertions;
using GdTracker.Core.Models;
using GdTracker.Data.Repositories;

namespace GdTracker.Tests;

public class AccountStatsRepositoryTests
{
    private static AccountStats Sample(long stars = 886) => new()
    {
        Stars = stars,
        Moons = 84,
        Demons = 17,
        OnlineLevelsCompleted = 322,
        OfficialLevelsCompleted = 27,
        SecretCoins = 84,
        Attempts = 43329,
        Jumps = 258487,
        TotalOrbs = 49359,
        RawValues = new Dictionary<string, long> { ["6"] = stars, ["99"] = 7 },
    };

    [Fact]
    public async Task Returns_null_when_no_snapshots_yet()
    {
        using var factory = new InMemorySqlite();
        var repo = new AccountStatsRepository(factory);

        (await repo.GetLatestAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Stores_first_snapshot()
    {
        using var factory = new InMemorySqlite();
        var repo = new AccountStatsRepository(factory);

        var saved = await repo.AddIfChangedAsync(Sample(), new DateTime(2026, 7, 18, 10, 0, 0, DateTimeKind.Utc));

        saved.Stars.Should().Be(886);
        saved.Jumps.Should().Be(258487);
        saved.SaveFileWrittenAt.Should().Be(new DateTime(2026, 7, 18, 10, 0, 0, DateTimeKind.Utc));
        (await repo.GetLatestAsync())!.Stars.Should().Be(886);
    }

    [Fact]
    public async Task Does_not_duplicate_unchanged_stats()
    {
        using var factory = new InMemorySqlite();
        var repo = new AccountStatsRepository(factory);

        await repo.AddIfChangedAsync(Sample(), null);
        await repo.AddIfChangedAsync(Sample(), null);

        await using var db = factory.CreateDbContext();
        db.AccountStatsSnapshots.Count().Should().Be(1);
    }

    [Fact]
    public async Task Stores_new_row_when_a_value_changed()
    {
        using var factory = new InMemorySqlite();
        var repo = new AccountStatsRepository(factory);

        await repo.AddIfChangedAsync(Sample(886), null);
        var second = await repo.AddIfChangedAsync(Sample(890), null);

        second.Stars.Should().Be(890);
        await using var db = factory.CreateDbContext();
        db.AccountStatsSnapshots.Count().Should().Be(2);
    }

    [Fact]
    public async Task Returns_existing_snapshot_when_nothing_changed()
    {
        using var factory = new InMemorySqlite();
        var repo = new AccountStatsRepository(factory);

        var first = await repo.AddIfChangedAsync(Sample(), null);
        var second = await repo.AddIfChangedAsync(Sample(), null);

        second.Id.Should().Be(first.Id);
    }

    [Fact]
    public async Task Latest_is_the_most_recent_by_captured_at()
    {
        using var factory = new InMemorySqlite();
        var repo = new AccountStatsRepository(factory);

        await repo.AddIfChangedAsync(Sample(886), null);
        await repo.AddIfChangedAsync(Sample(900), null);

        (await repo.GetLatestAsync())!.Stars.Should().Be(900);
    }

    [Fact]
    public async Task Archives_raw_values_as_json()
    {
        using var factory = new InMemorySqlite();
        var repo = new AccountStatsRepository(factory);

        var saved = await repo.AddIfChangedAsync(Sample(), null);

        saved.RawValuesJson.Should().Contain("\"99\":7");
    }

    [Fact]
    public async Task Save_file_time_alone_does_not_create_a_new_row()
    {
        using var factory = new InMemorySqlite();
        var repo = new AccountStatsRepository(factory);

        var time1 = new DateTime(2026, 7, 18, 10, 0, 0, DateTimeKind.Utc);
        var time2 = new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);

        await repo.AddIfChangedAsync(Sample(), time1);
        await repo.AddIfChangedAsync(Sample(), time2);

        // Проверяем, что строк осталось одна
        await using var db = factory.CreateDbContext();
        db.AccountStatsSnapshots.Count().Should().Be(1);

        // Проверяем, что SaveFileWrittenAt действительно обновился в БД через свежий запрос
        var latest = await repo.GetLatestAsync();
        latest.Should().NotBeNull();
        latest!.SaveFileWrittenAt.Should().Be(time2);
    }

    [Fact]
    public async Task Raw_values_change_alone_does_not_create_a_new_row()
    {
        using var factory = new InMemorySqlite();
        var repo = new AccountStatsRepository(factory);

        // Первый снимок с RawValues { "6": 886, "99": 7 }
        var stats1 = Sample(886);
        await repo.AddIfChangedAsync(stats1, null);

        // Второй снимок: все девять метрик те же, но добавился ключ "100" в RawValues
        var stats2 = new AccountStats
        {
            Stars = 886,
            Moons = 84,
            Demons = 17,
            OnlineLevelsCompleted = 322,
            OfficialLevelsCompleted = 27,
            SecretCoins = 84,
            Attempts = 43329,
            Jumps = 258487,
            TotalOrbs = 49359,
            RawValues = new Dictionary<string, long> { ["6"] = 886, ["99"] = 7, ["100"] = 999 },
        };

        await repo.AddIfChangedAsync(stats2, null);

        // Проверяем, что новая строка не создалась
        await using var db = factory.CreateDbContext();
        db.AccountStatsSnapshots.Count().Should().Be(1);
    }
}
