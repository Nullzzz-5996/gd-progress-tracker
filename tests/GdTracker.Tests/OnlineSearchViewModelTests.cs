using FluentAssertions;
using GdTracker.Core;
using GdTracker.Core.Abstractions;
using GdTracker.Core.Models;
using GdTracker.Data;
using GdTracker.Data.Repositories;
using GdTracker.ViewModels;

namespace GdTracker.Tests;

/// <summary>Заглушка онлайн-поиска: в тестах AddToTracker сама команда поиска не используется.</summary>
internal sealed class FakeGdLevelSearch : IGdLevelSearch
{
    public Task<IReadOnlyList<OnlineLevel>> SearchAsync(string query, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<OnlineLevel>>([]);
}

/// <summary>
/// Тесты сценария «Поиск онлайн» → «Добавить в трекер»: баг-репорт — уровень добавлялся
/// с нулевыми попытками, хотя игрок уже играл в него и сейв содержит прогресс.
/// </summary>
public class OnlineSearchViewModelTests
{
    private static OnlineLevel FoundLevel(long id = 13519) => new()
    {
        Id = id, Name = "Test Level", Creator = "SomeCreator", Difficulty = "Hard", Stars = 7,
    };

    private static SaveLevelDto SaveDto(long id, int normal, int attempts) => new()
    {
        GdLevelId = id, Name = "Test Level", Source = LevelSource.Online,
        BestNormalPercent = normal, Attempts = attempts,
    };

    private static (OnlineSearchViewModel vm, LevelRepository levels) Build(
        InMemorySqlite factory, FakeSaveReader reader, ISettingsService? settings = null)
    {
        var levels = new LevelRepository(factory);
        var importer = new SaveImportService(factory);
        var progressLookup = new SaveProgressLookupService(settings ?? new FakeSettings(), reader, importer);
        var vm = new OnlineSearchViewModel(new FakeGdLevelSearch(), levels, progressLookup);
        return (vm, levels);
    }

    [Fact]
    public async Task Adding_level_present_in_save_fills_attempts_and_best_percent()
    {
        using var factory = new InMemorySqlite();
        var reader = new FakeSaveReader(stats: null) { Levels = [SaveDto(13519, 72, 158)] };
        var (vm, levels) = Build(factory, reader);

        await vm.AddToTrackerCommand.ExecuteAsync(FoundLevel());

        var added = await levels.GetByGdLevelIdAsync(13519);
        added.Should().NotBeNull();
        added!.BestNormalPercent.Should().Be(72);
        added.TotalAttempts.Should().Be(158);
        vm.Status.Should().NotBeNullOrEmpty();
        vm.Status.Should().Contain("сейва");
    }

    [Fact]
    public async Task Adding_level_absent_from_save_still_adds_it_without_progress_or_exception()
    {
        using var factory = new InMemorySqlite();
        // Сейв читается успешно, но искомого уровня в нём нет.
        var reader = new FakeSaveReader(stats: null) { Levels = [SaveDto(999, 100, 10)] };
        var (vm, levels) = Build(factory, reader);

        await vm.AddToTrackerCommand.ExecuteAsync(FoundLevel());

        var added = await levels.GetByGdLevelIdAsync(13519);
        added.Should().NotBeNull();
        added!.TotalAttempts.Should().Be(0);
        added.BestNormalPercent.Should().Be(0);
        vm.Status.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Adding_level_when_save_unavailable_still_adds_it_without_exception()
    {
        using var factory = new InMemorySqlite();
        // Ни настроенного, ни автоопределённого пути — сейв недоступен вовсе.
        var reader = new FakeSaveReader(stats: null) { DefaultSaveFilePath = null };
        var (vm, levels) = Build(factory, reader);

        await vm.AddToTrackerCommand.ExecuteAsync(FoundLevel());

        var added = await levels.GetByGdLevelIdAsync(13519);
        added.Should().NotBeNull();
        vm.Status.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Adding_same_level_twice_reports_it_is_already_in_the_tracker()
    {
        using var factory = new InMemorySqlite();
        var reader = new FakeSaveReader(stats: null) { Levels = [SaveDto(13519, 72, 158)] };
        var (vm, levels) = Build(factory, reader);

        await vm.AddToTrackerCommand.ExecuteAsync(FoundLevel());
        await vm.AddToTrackerCommand.ExecuteAsync(FoundLevel());

        var all = await levels.GetAllAsync();
        all.Should().ContainSingle();
        vm.Status.Should().Contain("уже в трекере");
    }
}
