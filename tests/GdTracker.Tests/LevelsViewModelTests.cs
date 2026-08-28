using FluentAssertions;
using GdTracker.Core;
using GdTracker.Core.Models;
using GdTracker.Data;
using GdTracker.Data.Repositories;
using GdTracker.ViewModels;

namespace GdTracker.Tests;

/// <summary>
/// Тесты вкладки «Уровни»: импортированные из игры уровни лежат в базе (их видит
/// статистика), но в сетке не показываются, пока пользователь не добавит уровень
/// вручную — тогда ручное добавление «усыновляет» скрытую строку со всеми данными.
/// </summary>
public class LevelsViewModelTests
{
    private static LevelsViewModel BuildVm(InMemorySqlite factory, out LevelRepository levels)
    {
        levels = new LevelRepository(factory);
        var reader = new FakeSaveReader(stats: null);
        var importer = new SaveImportService(factory);
        return new LevelsViewModel(
            levels, new ProgressRepository(factory), reader, importer,
            new ProgressSharingService(factory), new NullFileDialog(), new FakeConfirmation(),
            new FakeSettings(),
            new SaveProgressLookupService(new FakeSettings(), reader, importer));
    }

    private static SaveLevelDto Dto(
        long id, string? name, int normal = 0, int practice = 0, int attempts = 0)
        => new()
        {
            GdLevelId = id,
            Name = name,
            Source = LevelSource.Online,
            BestNormalPercent = normal,
            BestPracticePercent = practice,
            Attempts = attempts,
            Stars = 10,
            Creator = "Riot",
            Difficulty = "Extreme Demon",
        };

    [Fact]
    public async Task Imported_levels_stay_in_the_database_but_out_of_the_list()
    {
        using var factory = new InMemorySqlite();
        var vm = BuildVm(factory, out var levels);
        await new SaveImportService(factory).ImportAsync([Dto(10565740, "Bloodbath", 6, 100, 2180)]);

        await vm.LoadAsync();

        vm.Levels.Should().BeEmpty();
        (await levels.GetAllAsync()).Should().ContainSingle("данные импорта нужны статистике");
    }

    [Fact]
    public async Task Adding_a_level_by_gd_id_adopts_the_imported_row_with_all_its_data()
    {
        using var factory = new InMemorySqlite();
        var vm = BuildVm(factory, out var levels);
        await new SaveImportService(factory).ImportAsync([Dto(10565740, "Bloodbath", 6, 100, 2180)]);
        await vm.LoadAsync();

        vm.NewLevelName = "бладбас";
        vm.NewLevelGdId = "10565740";
        await vm.AddLevelCommand.ExecuteAsync(null);

        vm.Error.Should().BeNull();
        var row = vm.Levels.Should().ContainSingle().Subject.Level;
        row.GdLevelId.Should().Be(10565740);
        row.Name.Should().Be("Bloodbath", "название из игры точнее введённого вручную");
        row.BestNormalPercent.Should().Be(6);
        row.BestPracticePercent.Should().Be(100);
        row.TotalAttempts.Should().Be(2180);
        row.Stars.Should().Be(10);
        row.Creator.Should().Be("Riot");
        row.Difficulty.Should().Be("Extreme Demon");
        (await levels.GetAllAsync()).Should().ContainSingle("вторая строка не заводится");
    }

    [Fact]
    public async Task Adopted_level_without_a_name_in_the_save_takes_the_name_the_user_typed()
    {
        using var factory = new InMemorySqlite();
        var vm = BuildVm(factory, out _);
        await new SaveImportService(factory).ImportAsync([Dto(555, name: null, normal: 20, attempts: 8)]);
        await vm.LoadAsync();

        vm.NewLevelName = "Мой уровень";
        vm.NewLevelGdId = "555";
        await vm.AddLevelCommand.ExecuteAsync(null);

        vm.Levels.Should().ContainSingle().Subject.Level.Name.Should().Be("Мой уровень");
    }

    [Fact]
    public async Task Adding_a_level_by_name_adopts_the_most_played_imported_row()
    {
        using var factory = new InMemorySqlite();
        var vm = BuildVm(factory, out _);
        await new SaveImportService(factory).ImportAsync(
            [Dto(1, "Bloodbath", 6, 100, 2180), Dto(2, "bloodbath", 3, 0, 40)]);
        await vm.LoadAsync();

        vm.NewLevelName = "BLOODBATH";
        await vm.AddLevelCommand.ExecuteAsync(null);

        vm.Error.Should().BeNull();
        var row = vm.Levels.Should().ContainSingle().Subject.Level;
        row.GdLevelId.Should().Be(1);
        row.TotalAttempts.Should().Be(2180);
    }

    [Fact]
    public async Task Adding_a_level_that_is_already_in_the_list_reports_a_duplicate()
    {
        using var factory = new InMemorySqlite();
        var vm = BuildVm(factory, out var levels);
        await levels.AddAsync(new Level { Name = "Bloodbath", GdLevelId = 10565740, Source = LevelSource.Online });
        await vm.LoadAsync();

        vm.NewLevelName = "Bloodbath";
        vm.NewLevelGdId = "10565740";
        await vm.AddLevelCommand.ExecuteAsync(null);

        vm.Error.Should().Contain("уже есть");
        vm.Levels.Should().ContainSingle();
    }

    [Fact]
    public async Task Adding_a_level_that_the_save_never_had_creates_a_new_one()
    {
        using var factory = new InMemorySqlite();
        var vm = BuildVm(factory, out _);
        await vm.LoadAsync();

        vm.NewLevelName = "Мой уровень";
        await vm.AddLevelCommand.ExecuteAsync(null);

        vm.Error.Should().BeNull();
        vm.Levels.Should().ContainSingle().Subject.Level.Name.Should().Be("Мой уровень");
    }
}
