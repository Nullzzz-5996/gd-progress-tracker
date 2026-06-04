using FluentAssertions;
using GdTracker.Core;
using GdTracker.Core.Models;
using GdTracker.Data;
using GdTracker.Data.Repositories;

namespace GdTracker.Tests;

public class SaveImportServiceTests
{
    private static SaveLevelDto Dto(long id, string name, LevelSource src,
        int normal, int practice, int attempts, int? stars = null)
        => new()
        {
            GdLevelId = id, Name = name, Source = src,
            BestNormalPercent = normal, BestPracticePercent = practice,
            Attempts = attempts, Stars = stars,
        };

    [Fact]
    public async Task Import_creates_levels_with_best_percents_and_attempts()
    {
        using var factory = new InMemorySqlite();
        var importer = new SaveImportService(factory);
        var levels = new LevelRepository(factory);

        var result = await importer.ImportAsync(new[]
        {
            Dto(1, "Stereo Madness", LevelSource.Official, 100, 100, 30, stars: 1),
            Dto(10565740, "Bloodbath", LevelSource.Online, 6, 100, 2180, stars: 10),
        });

        result.LevelsAdded.Should().Be(2);
        result.LevelsUpdated.Should().Be(0);

        var all = await levels.GetAllAsync();
        all.Should().HaveCount(2);

        var bloodbath = all.Single(l => l.GdLevelId == 10565740);
        bloodbath.Name.Should().Be("Bloodbath");
        bloodbath.Source.Should().Be(LevelSource.Online);
        bloodbath.BestNormalPercent.Should().Be(6);
        bloodbath.BestPracticePercent.Should().Be(100);
        bloodbath.TotalAttempts.Should().Be(2180);
        bloodbath.Stars.Should().Be(10);
        bloodbath.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task Reimport_is_idempotent_no_duplicates()
    {
        using var factory = new InMemorySqlite();
        var importer = new SaveImportService(factory);
        var levels = new LevelRepository(factory);

        var data = new[] { Dto(1, "Stereo Madness", LevelSource.Official, 100, 100, 30, 1) };

        await importer.ImportAsync(data);
        var second = await importer.ImportAsync(data);

        second.LevelsAdded.Should().Be(0);
        second.LevelsUpdated.Should().Be(1);

        var all = await levels.GetAllAsync();
        all.Should().HaveCount(1);

        var level = await levels.GetByIdAsync(all[0].Id);
        // Только импортные записи (normal + practice), без дублей.
        level!.ProgressRecords.Should().OnlyContain(r => r.Source == ProgressSource.SaveImport);
        level.ProgressRecords.Should().HaveCount(2);
        level.BestNormalPercent.Should().Be(100);
    }

    [Fact]
    public async Task Reimport_updates_changed_best_percent_and_attempts()
    {
        using var factory = new InMemorySqlite();
        var importer = new SaveImportService(factory);
        var levels = new LevelRepository(factory);

        await importer.ImportAsync(new[] { Dto(99, "WIP", LevelSource.Online, 40, 50, 100) });
        await importer.ImportAsync(new[] { Dto(99, "WIP", LevelSource.Online, 73, 90, 250) });

        var level = (await levels.GetAllAsync()).Single();
        level.BestNormalPercent.Should().Be(73);
        level.BestPracticePercent.Should().Be(90);
        level.TotalAttempts.Should().Be(250);
    }

    [Fact]
    public async Task Import_preserves_manual_records_and_combines_with_save()
    {
        using var factory = new InMemorySqlite();
        var importer = new SaveImportService(factory);
        var levels = new LevelRepository(factory);
        var progress = new ProgressRepository(factory);

        // Существующий уровень с ручной записью лучше, чем сейв.
        var level = await levels.AddAsync(new Level { Name = "Manual", GdLevelId = 555, Source = LevelSource.Online });
        await progress.AddAsync(new ProgressRecord
        {
            LevelId = level.Id, Type = RunType.FromZero, StartPercent = 0,
            ReachedPercent = 85, Mode = ProgressMode.Normal, Attempts = 40,
            Source = ProgressSource.Manual,
        });

        // Сейв даёт normal=60 (хуже ручного 85), attempts=200.
        await importer.ImportAsync(new[] { Dto(555, "Manual", LevelSource.Online, 60, 0, 200) });

        var reloaded = await levels.GetByIdAsync(level.Id);
        reloaded!.BestNormalPercent.Should().Be(85);             // ручной максимум сохранён
        reloaded.TotalAttempts.Should().Be(240);                 // 40 ручных + 200 из сейва
        reloaded.ProgressRecords.Should().Contain(r => r.Source == ProgressSource.Manual);
        reloaded.ProgressRecords.Should().Contain(r => r.Source == ProgressSource.SaveImport);
    }

    [Fact]
    public async Task Practice_only_level_still_records_attempts()
    {
        using var factory = new InMemorySqlite();
        var importer = new SaveImportService(factory);
        var levels = new LevelRepository(factory);

        await importer.ImportAsync(new[] { Dto(7, "PracticeOnly", LevelSource.Online, 0, 30, 120) });

        var level = (await levels.GetAllAsync()).Single();
        level.BestPracticePercent.Should().Be(30);
        level.TotalAttempts.Should().Be(120);
    }
}
