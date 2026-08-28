using FluentAssertions;
using GdTracker.Core;
using GdTracker.Core.Models;
using GdTracker.Data;
using GdTracker.Data.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GdTracker.Tests;

/// <summary>
/// Интеграционные тесты репозиториев на SQLite in-memory: проверяют, что
/// добавление прогресса пересчитывает агрегаты уровня и данные сохраняются.
/// </summary>
public class RepositoryTests
{
    /// <summary>Фабрика контекстов поверх общего открытого in-memory соединения.</summary>
    private sealed class InMemoryFactory : IDbContextFactory<AppDbContext>, IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<AppDbContext> _options;

        public InMemoryFactory()
        {
            _connection = new SqliteConnection("Filename=:memory:");
            _connection.Open();
            _options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(_connection)
                .Options;

            using var ctx = new AppDbContext(_options);
            ctx.Database.EnsureCreated();
        }

        public AppDbContext CreateDbContext() => new(_options);

        public void Dispose() => _connection.Dispose();
    }

    [Fact]
    public async Task AddProgress_recomputes_level_aggregates_and_persists()
    {
        using var factory = new InMemoryFactory();
        var levels = new LevelRepository(factory);
        var progress = new ProgressRepository(factory);

        var level = await levels.AddAsync(new Level { Name = "Stereo Madness", Source = LevelSource.Official });

        await progress.AddAsync(new ProgressRecord
        {
            LevelId = level.Id,
            Type = RunType.FromZero,
            StartPercent = 0,
            ReachedPercent = 45,
            Mode = ProgressMode.Normal,
            Attempts = 12,
        });
        await progress.AddAsync(new ProgressRecord
        {
            LevelId = level.Id,
            Type = RunType.Segment,
            StartPercent = 30,
            ReachedPercent = 60,
            Mode = ProgressMode.Practice,
            Attempts = 8,
        });

        var reloaded = await levels.GetByIdAsync(level.Id);

        reloaded.Should().NotBeNull();
        reloaded!.BestNormalPercent.Should().Be(45);
        reloaded.BestPracticePercent.Should().Be(60);
        reloaded.TotalAttempts.Should().Be(20);
        reloaded.IsCompleted.Should().BeFalse();
        reloaded.ProgressRecords.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAll_returns_added_levels()
    {
        using var factory = new InMemoryFactory();
        var levels = new LevelRepository(factory);

        await levels.AddAsync(new Level { Name = "Back On Track" });
        await levels.AddAsync(new Level { Name = "Polargeist" });

        var all = await levels.GetAllAsync();

        all.Should().HaveCount(2);
        all.Select(l => l.Name).Should().Contain(new[] { "Back On Track", "Polargeist" });
    }

    [Fact]
    public async Task DeleteProgress_recomputes_level_aggregates()
    {
        using var factory = new InMemoryFactory();
        var levels = new LevelRepository(factory);
        var progress = new ProgressRepository(factory);

        var level = await levels.AddAsync(new Level { Name = "Dry Out" });
        var first = await progress.AddAsync(new ProgressRecord
        {
            LevelId = level.Id, Type = RunType.FromZero, StartPercent = 0,
            ReachedPercent = 70, Mode = ProgressMode.Normal, Attempts = 5,
        });
        await progress.AddAsync(new ProgressRecord
        {
            LevelId = level.Id, Type = RunType.FromZero, StartPercent = 0,
            ReachedPercent = 40, Mode = ProgressMode.Normal, Attempts = 3,
        });

        await progress.DeleteAsync(first.Id);

        var reloaded = await levels.GetByIdAsync(level.Id);
        reloaded!.BestNormalPercent.Should().Be(40);
        reloaded.TotalAttempts.Should().Be(3);
        reloaded.ProgressRecords.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetTracked_returns_only_levels_visible_in_the_list()
    {
        using var factory = new InMemoryFactory();
        var levels = new LevelRepository(factory);
        await levels.AddAsync(new Level { Name = "Visible" });
        await levels.AddAsync(new Level { Name = "Hidden", GdLevelId = 7, IsTracked = false });

        (await levels.GetAllAsync()).Should().HaveCount(2);
        (await levels.GetTrackedAsync()).Should().ContainSingle().Which.Name.Should().Be("Visible");
    }

    [Fact]
    public async Task FindUntrackedByName_ignores_case_and_prefers_the_most_played()
    {
        using var factory = new InMemoryFactory();
        var levels = new LevelRepository(factory);
        await levels.AddAsync(new Level { Name = "Bloodbath", GdLevelId = 1, TotalAttempts = 40, IsTracked = false });
        await levels.AddAsync(new Level { Name = "bloodbath", GdLevelId = 2, TotalAttempts = 2180, IsTracked = false });
        await levels.AddAsync(new Level { Name = "Cataclysm", GdLevelId = 3 });

        var found = await levels.FindUntrackedByNameAsync("BLOODBATH");

        found.Should().NotBeNull();
        found!.GdLevelId.Should().Be(2);
        (await levels.FindUntrackedByNameAsync("Cataclysm")).Should().BeNull("видимые уровни не усыновляются");
        (await levels.FindUntrackedByNameAsync("Deadlocked")).Should().BeNull();
    }
}
