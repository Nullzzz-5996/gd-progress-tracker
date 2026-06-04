using FluentAssertions;
using GdTracker.Core;
using GdTracker.Core.Models;
using GdTracker.Data;
using GdTracker.Data.Repositories;

namespace GdTracker.Tests;

public class ProgressSharingServiceTests
{
    private static async Task SeedLevelWithRecord(InMemorySqlite db, string name, long? gdId, int reached)
    {
        var levels = new LevelRepository(db);
        var progress = new ProgressRepository(db);
        var level = await levels.AddAsync(new Level { Name = name, GdLevelId = gdId, Source = LevelSource.Online });
        await progress.AddAsync(new ProgressRecord
        {
            LevelId = level.Id, Type = RunType.FromZero, StartPercent = 0, ReachedPercent = reached,
            Mode = ProgressMode.Normal, Attempts = 100, Source = ProgressSource.Manual,
            Date = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc),
        });
    }

    [Fact]
    public async Task Export_then_import_into_fresh_db_reproduces_data()
    {
        using var src = new InMemorySqlite();
        await SeedLevelWithRecord(src, "Bloodbath", 10565740, reached: 6);

        var file = Path.GetTempFileName();
        try
        {
            await new ProgressSharingService(src).ExportAsync(file);

            using var dst = new InMemorySqlite();
            var summary = await new ProgressSharingService(dst).ImportAsync(file);

            summary.LevelsAdded.Should().Be(1);
            summary.RecordsAdded.Should().Be(1);

            var levels = new LevelRepository(dst);
            var imported = (await levels.GetAllAsync()).Single();
            imported.Name.Should().Be("Bloodbath");
            imported.GdLevelId.Should().Be(10565740);
            imported.BestNormalPercent.Should().Be(6);
            imported.TotalAttempts.Should().Be(100);

            var full = await levels.GetByIdAsync(imported.Id);
            full!.ProgressRecords.Should().ContainSingle();
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task Reimport_same_file_does_not_duplicate_records()
    {
        using var src = new InMemorySqlite();
        await SeedLevelWithRecord(src, "Bloodbath", 10565740, reached: 6);

        var file = Path.GetTempFileName();
        try
        {
            await new ProgressSharingService(src).ExportAsync(file);

            using var dst = new InMemorySqlite();
            var service = new ProgressSharingService(dst);
            await service.ImportAsync(file);
            var second = await service.ImportAsync(file);

            second.RecordsAdded.Should().Be(0);
            second.LevelsUpdated.Should().Be(1);

            var levels = new LevelRepository(dst);
            var imported = (await levels.GetAllAsync()).Single();
            var full = await levels.GetByIdAsync(imported.Id);
            full!.ProgressRecords.Should().ContainSingle();
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task Import_merges_into_existing_level_by_gd_id()
    {
        using var src = new InMemorySqlite();
        await SeedLevelWithRecord(src, "Bloodbath", 10565740, reached: 6);

        var file = Path.GetTempFileName();
        try
        {
            await new ProgressSharingService(src).ExportAsync(file);

            using var dst = new InMemorySqlite();
            // У получателя уже есть этот уровень с меньшим прогрессом.
            await SeedLevelWithRecord(dst, "Bloodbath", 10565740, reached: 3);

            var summary = await new ProgressSharingService(dst).ImportAsync(file);
            summary.LevelsAdded.Should().Be(0);
            summary.LevelsUpdated.Should().Be(1);
            summary.RecordsAdded.Should().Be(1);

            var levels = new LevelRepository(dst);
            var merged = (await levels.GetAllAsync()).Single();
            merged.BestNormalPercent.Should().Be(6);   // из импорта (6 > 3)
            var full = await levels.GetByIdAsync(merged.Id);
            full!.ProgressRecords.Should().HaveCount(2);
        }
        finally { File.Delete(file); }
    }
}
