using FluentAssertions;
using GdTracker.Core.Models;
using GdTracker.Data.Repositories;

namespace GdTracker.Tests;

/// <summary>Интеграционные тесты репозитория строк таблицы «Прогрессы» на SQLite in-memory.</summary>
public class LevelProgressRowRepositoryTests
{
    [Fact]
    public async Task Add_and_get_by_level_returns_rows_ordered_by_position()
    {
        using var factory = new InMemorySqlite();
        var levels = new LevelRepository(factory);
        var rows = new LevelProgressRowRepository(factory);

        var level = await levels.AddAsync(new Level { Name = "Bloodbath" });
        await rows.AddAsync(new LevelProgressRow { LevelId = level.Id, Position = 2, PracticeAttempts = "23" });
        await rows.AddAsync(new LevelProgressRow { LevelId = level.Id, Position = 1, SegmentRange = "24-35" });

        var loaded = await rows.GetByLevelAsync(level.Id);

        loaded.Should().HaveCount(2);
        loaded.Select(r => r.Position).Should().ContainInOrder(1, 2);
    }

    [Fact]
    public async Task Update_persists_edited_columns()
    {
        using var factory = new InMemorySqlite();
        var levels = new LevelRepository(factory);
        var rows = new LevelProgressRowRepository(factory);
        var level = await levels.AddAsync(new Level { Name = "Test" });
        var row = await rows.AddAsync(new LevelProgressRow { LevelId = level.Id, Position = 1 });

        row.PracticeAttempts = "42";
        row.ToHundredRange = "67-100";
        await rows.UpdateAsync(row);

        var loaded = await rows.GetByLevelAsync(level.Id);
        loaded.Single().PracticeAttempts.Should().Be("42");
        loaded.Single().ToHundredRange.Should().Be("67-100");
    }

    [Fact]
    public async Task Delete_removes_row()
    {
        using var factory = new InMemorySqlite();
        var levels = new LevelRepository(factory);
        var rows = new LevelProgressRowRepository(factory);
        var level = await levels.AddAsync(new Level { Name = "Test" });
        var row = await rows.AddAsync(new LevelProgressRow { LevelId = level.Id, Position = 1 });

        await rows.DeleteAsync(row.Id);

        (await rows.GetByLevelAsync(level.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task Deleting_level_cascades_to_progress_rows()
    {
        using var factory = new InMemorySqlite();
        var levels = new LevelRepository(factory);
        var rows = new LevelProgressRowRepository(factory);
        var level = await levels.AddAsync(new Level { Name = "Test" });
        await rows.AddAsync(new LevelProgressRow { LevelId = level.Id, Position = 1 });

        await levels.DeleteManyAsync(new[] { level.Id });

        (await rows.GetByLevelAsync(level.Id)).Should().BeEmpty();
    }
}
