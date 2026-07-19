using FluentAssertions;
using GdTracker.Core;
using GdTracker.Core.Abstractions;
using GdTracker.Core.Models;
using GdTracker.Data;
using GdTracker.Data.Repositories;
using GdTracker.GameSync;
using GdTracker.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace GdTracker.Tests;

/// <summary>
/// End-to-end сценарий Фазы 1 через view-модели и реальный файл БД:
/// добавление уровня, логирование прогрессов «с нуля» и «сегмент», пересчёт
/// агрегатов и сохранение данных между «перезапусками» (новое подключение к файлу).
/// </summary>
public class LevelsWorkflowTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(), $"gdtracker_test_{Guid.NewGuid():N}.db");

    /// <summary>Фабрика контекстов над файлом БД (имитирует реальную работу приложения).</summary>
    private sealed class FileFactory : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _options;

        public FileFactory(string path)
        {
            _options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={path}")
                .Options;

            using var ctx = new AppDbContext(_options);
            ctx.Database.Migrate();
        }

        public AppDbContext CreateDbContext() => new(_options);
    }

    [Fact]
    public async Task Add_level_log_two_progress_records_aggregates_update_and_persist()
    {
        int levelId;

        // --- Сессия 1: ввод данных через view-модели ---
        {
            var factory = new FileFactory(_dbPath);
            var levels = new LevelRepository(factory);
            var progress = new ProgressRepository(factory);

            var levelsVm = new LevelsViewModel(
                levels, progress, new SaveFileReader(), new SaveImportService(factory),
                new ProgressSharingService(factory), new NullFileDialog(), new FakeConfirmation(), new FakeSettings(),
                new SaveProgressLookupService(new FakeSettings(), new SaveFileReader(), new SaveImportService(factory)));
            await levelsVm.LoadAsync();

            levelsVm.NewLevelName = "Bloodbath";
            levelsVm.NewLevelSource = LevelSource.Online;
            await levelsVm.AddLevelCommand.ExecuteAsync(null);

            levelsVm.Levels.Should().ContainSingle();
            levelsVm.SelectedRow.Should().NotBeNull();
            levelId = levelsVm.SelectedRow!.Level.Id;

            // Деталь грузим явно (в UI это делает OnSelectedLevelChanged).
            var detail = new LevelDetailViewModel(levels, progress);
            await detail.LoadAsync(levelId);

            // Прогресс «с нуля»: 0 -> 45%, normal, 12 попыток.
            detail.NewRunType = RunType.FromZero;
            detail.NewMode = ProgressMode.Normal;
            detail.NewReachedPercent = 45;
            detail.NewAttempts = "12";
            await detail.AddProgressCommand.ExecuteAsync(null);
            detail.FormError.Should().BeNull();

            // Прогресс «сегмент»: 30 -> 60%, practice, 8 попыток.
            detail.NewRunType = RunType.Segment;
            detail.NewStartPercent = 30;
            detail.NewMode = ProgressMode.Practice;
            detail.NewReachedPercent = 60;
            detail.NewAttempts = "8";
            await detail.AddProgressCommand.ExecuteAsync(null);
            detail.FormError.Should().BeNull();

            detail.Records.Should().HaveCount(2);
        }

        // --- Сессия 2: «перезапуск» — новое подключение к тому же файлу ---
        {
            var factory = new FileFactory(_dbPath);
            var levels = new LevelRepository(factory);

            var reloaded = await levels.GetByIdAsync(levelId);

            reloaded.Should().NotBeNull();
            reloaded!.BestNormalPercent.Should().Be(45);
            reloaded.BestPracticePercent.Should().Be(60);
            reloaded.TotalAttempts.Should().Be(20);
            reloaded.IsCompleted.Should().BeFalse();
            reloaded.ProgressRecords.Should().HaveCount(2);
        }
    }

    [Fact]
    public async Task Invalid_progress_sets_form_error_and_is_not_saved()
    {
        var factory = new FileFactory(_dbPath);
        var levels = new LevelRepository(factory);
        var progress = new ProgressRepository(factory);

        var level = await levels.AddAsync(new Level { Name = "Test" });
        var detail = new LevelDetailViewModel(levels, progress);
        await detail.LoadAsync(level.Id);

        // FromZero с reached=0 недопустим (reached должен быть > start).
        detail.NewRunType = RunType.FromZero;
        detail.NewReachedPercent = 0;
        await detail.AddProgressCommand.ExecuteAsync(null);

        detail.FormError.Should().NotBeNull();
        detail.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task Levels_vm_construction_with_saved_path_does_not_call_set_save_file_path()
    {
        var factory = new FileFactory(_dbPath);
        var levels = new LevelRepository(factory);
        var progress = new ProgressRepository(factory);
        var settings = new FakeSettings();
        settings.SetSaveFilePath(@"C:\custom\CCGameManager.dat");
        settings.SetSaveFilePathCallCount = 0; // сбрасываем счётчик

        var vm = new LevelsViewModel(
            levels, progress, new SaveFileReader(), new SaveImportService(factory),
            new ProgressSharingService(factory), new NullFileDialog(), new FakeConfirmation(), settings,
            new SaveProgressLookupService(settings, new SaveFileReader(), new SaveImportService(factory)));

        // При конструировании не должно быть вызовов SetSaveFilePath.
        settings.SetSaveFilePathCallCount.Should().Be(0);
        vm.SaveFilePath.Should().Be(@"C:\custom\CCGameManager.dat");
    }

    [Fact]
    public async Task Levels_vm_property_change_calls_set_save_file_path()
    {
        var factory = new FileFactory(_dbPath);
        var levels = new LevelRepository(factory);
        var progress = new ProgressRepository(factory);
        var settings = new FakeSettings();
        settings.SetSaveFilePath(@"C:\initial\CCGameManager.dat");
        settings.SetSaveFilePathCallCount = 0;

        var vm = new LevelsViewModel(
            levels, progress, new SaveFileReader(), new SaveImportService(factory),
            new ProgressSharingService(factory), new NullFileDialog(), new FakeConfirmation(), settings,
            new SaveProgressLookupService(settings, new SaveFileReader(), new SaveImportService(factory)));

        // Изменение свойства после конструирования должно вызвать SetSaveFilePath.
        vm.SaveFilePath = @"C:\new\CCGameManager.dat";

        settings.SetSaveFilePathCallCount.Should().Be(1);
        settings.SaveFilePath.Should().Be(@"C:\new\CCGameManager.dat");
    }

    [Fact]
    public async Task Bulk_delete_declined_confirmation_leaves_levels_and_skips_repository()
    {
        var factory = new FileFactory(_dbPath);
        var innerLevels = new LevelRepository(factory);
        var countingLevels = new DeleteCountingLevelRepository(innerLevels);
        var progress = new ProgressRepository(factory);
        var confirmation = new FakeConfirmation { Result = false };

        var vm = new LevelsViewModel(
            countingLevels, progress, new SaveFileReader(), new SaveImportService(factory),
            new ProgressSharingService(factory), new NullFileDialog(), confirmation, new FakeSettings(),
            new SaveProgressLookupService(new FakeSettings(), new SaveFileReader(), new SaveImportService(factory)));

        await vm.LoadAsync();
        vm.NewLevelName = "Level A";
        await vm.AddLevelCommand.ExecuteAsync(null);
        vm.NewLevelName = "Level B";
        await vm.AddLevelCommand.ExecuteAsync(null);

        vm.SelectAllCommand.Execute(null);
        vm.SelectedCount.Should().Be(2);

        await vm.DeleteSelectedCommand.ExecuteAsync(null);

        confirmation.CallCount.Should().Be(1);
        confirmation.LastMessage.Should().Contain("2");
        countingLevels.DeleteManyAsyncCallCount.Should().Be(0);
        vm.Levels.Should().HaveCount(2);

        var remaining = await innerLevels.GetAllAsync();
        remaining.Should().HaveCount(2);
    }

    [Fact]
    public async Task Bulk_delete_confirmed_removes_levels()
    {
        var factory = new FileFactory(_dbPath);
        var levels = new LevelRepository(factory);
        var progress = new ProgressRepository(factory);
        var confirmation = new FakeConfirmation { Result = true };

        var vm = new LevelsViewModel(
            levels, progress, new SaveFileReader(), new SaveImportService(factory),
            new ProgressSharingService(factory), new NullFileDialog(), confirmation, new FakeSettings(),
            new SaveProgressLookupService(new FakeSettings(), new SaveFileReader(), new SaveImportService(factory)));

        await vm.LoadAsync();
        vm.NewLevelName = "Level A";
        await vm.AddLevelCommand.ExecuteAsync(null);
        vm.NewLevelName = "Level B";
        await vm.AddLevelCommand.ExecuteAsync(null);

        vm.SelectAllCommand.Execute(null);
        await vm.DeleteSelectedCommand.ExecuteAsync(null);

        confirmation.CallCount.Should().Be(1);
        vm.Levels.Should().BeEmpty();

        var remaining = await levels.GetAllAsync();
        remaining.Should().BeEmpty();
    }

    [Fact]
    public async Task Single_delete_declined_confirmation_leaves_level_and_skips_repository()
    {
        var factory = new FileFactory(_dbPath);
        var innerLevels = new LevelRepository(factory);
        var countingLevels = new DeleteCountingLevelRepository(innerLevels);
        var progress = new ProgressRepository(factory);
        var confirmation = new FakeConfirmation { Result = false };

        var vm = new LevelsViewModel(
            countingLevels, progress, new SaveFileReader(), new SaveImportService(factory),
            new ProgressSharingService(factory), new NullFileDialog(), confirmation, new FakeSettings(),
            new SaveProgressLookupService(new FakeSettings(), new SaveFileReader(), new SaveImportService(factory)));

        await vm.LoadAsync();
        vm.NewLevelName = "Solo Level";
        await vm.AddLevelCommand.ExecuteAsync(null);
        vm.SelectedRow.Should().NotBeNull();

        await vm.DeleteCurrentLevelCommand.ExecuteAsync(null);

        confirmation.CallCount.Should().Be(1);
        countingLevels.DeleteAsyncCallCount.Should().Be(0);
        vm.Levels.Should().ContainSingle();
        vm.SelectedRow.Should().NotBeNull();

        var remaining = await innerLevels.GetAllAsync();
        remaining.Should().ContainSingle();
    }

    [Fact]
    public async Task Single_delete_confirmed_removes_level()
    {
        var factory = new FileFactory(_dbPath);
        var levels = new LevelRepository(factory);
        var progress = new ProgressRepository(factory);
        var confirmation = new FakeConfirmation { Result = true };

        var vm = new LevelsViewModel(
            levels, progress, new SaveFileReader(), new SaveImportService(factory),
            new ProgressSharingService(factory), new NullFileDialog(), confirmation, new FakeSettings(),
            new SaveProgressLookupService(new FakeSettings(), new SaveFileReader(), new SaveImportService(factory)));

        await vm.LoadAsync();
        vm.NewLevelName = "Solo Level";
        await vm.AddLevelCommand.ExecuteAsync(null);

        await vm.DeleteCurrentLevelCommand.ExecuteAsync(null);

        confirmation.CallCount.Should().Be(1);
        confirmation.LastMessage.Should().Contain("Solo Level");
        vm.Levels.Should().BeEmpty();
        vm.SelectedRow.Should().BeNull();

        var remaining = await levels.GetAllAsync();
        remaining.Should().BeEmpty();
    }

    public void Dispose()
    {
        foreach (var f in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            try { if (File.Exists(f)) File.Delete(f); }
            catch { /* игнорируем — временный файл */ }
        }
    }
}

/// <summary>
/// Тесты бага «попытки не появляются на уровне, добавленном вручную»: форма ручного
/// добавления не задавала GdLevelId, из-за чего SaveImportService (сопоставляющий строки
/// только по GdLevelId) никогда не находил такую строку и заводил рядом вторую.
/// </summary>
public class LevelsViewModelManualAddGdIdTests
{
    private static SaveLevelDto SaveDto(long id, int normal, int attempts) => new()
    {
        GdLevelId = id, Name = "The Nightmare", Source = LevelSource.Online,
        BestNormalPercent = normal, Attempts = attempts,
    };

    private static (LevelsViewModel vm, LevelRepository levels) BuildVm(
        InMemorySqlite factory, FakeSaveReader reader, ISettingsService? settings = null)
    {
        settings ??= new FakeSettings();
        var levels = new LevelRepository(factory);
        var progress = new ProgressRepository(factory);
        var importer = new SaveImportService(factory);
        var progressLookup = new SaveProgressLookupService(settings, reader, importer);
        var vm = new LevelsViewModel(
            levels, progress, reader, importer,
            new ProgressSharingService(factory), new NullFileDialog(), new FakeConfirmation(), settings,
            progressLookup);
        return (vm, levels);
    }

    [Fact]
    public async Task Manual_add_with_gd_id_present_in_save_pulls_attempts_and_best_percent()
    {
        using var factory = new InMemorySqlite();
        var reader = new FakeSaveReader(stats: null) { Levels = [SaveDto(13519, 72, 158)] };
        var (vm, levels) = BuildVm(factory, reader);
        await vm.LoadAsync();

        vm.NewLevelName = "The Nightmare";
        vm.NewLevelGdId = "13519";
        await vm.AddLevelCommand.ExecuteAsync(null);

        vm.Error.Should().BeNull();
        var added = await levels.GetByGdLevelIdAsync(13519);
        added.Should().NotBeNull();
        added!.BestNormalPercent.Should().Be(72);
        added.TotalAttempts.Should().Be(158);

        // Поля формы очищаются так же, как и название.
        vm.NewLevelGdId.Should().BeEmpty();
        vm.NewLevelName.Should().BeEmpty();
    }

    [Fact]
    public async Task Manual_add_with_gd_id_absent_from_save_creates_level_without_progress_or_exception()
    {
        using var factory = new InMemorySqlite();
        // Сейв читается успешно, но искомого уровня в нём нет.
        var reader = new FakeSaveReader(stats: null) { Levels = [SaveDto(999, 100, 10)] };
        var (vm, levels) = BuildVm(factory, reader);
        await vm.LoadAsync();

        vm.NewLevelName = "The Nightmare";
        vm.NewLevelGdId = "13519";
        await vm.AddLevelCommand.ExecuteAsync(null);

        vm.Error.Should().BeNull();
        var added = await levels.GetByGdLevelIdAsync(13519);
        added.Should().NotBeNull();
        added!.TotalAttempts.Should().Be(0);
        added.BestNormalPercent.Should().Be(0);
    }

    [Fact]
    public async Task Manual_add_without_gd_id_keeps_previous_behavior()
    {
        using var factory = new InMemorySqlite();
        var reader = new FakeSaveReader(stats: null);
        var (vm, levels) = BuildVm(factory, reader);
        await vm.LoadAsync();

        vm.NewLevelName = "Мои личные заметки";
        await vm.AddLevelCommand.ExecuteAsync(null);

        vm.Error.Should().BeNull();
        var all = await levels.GetAllAsync();
        all.Should().ContainSingle();
        all[0].GdLevelId.Should().BeNull();
    }

    [Fact]
    public async Task Manual_add_with_non_numeric_gd_id_sets_error_and_does_not_create_level()
    {
        using var factory = new InMemorySqlite();
        var reader = new FakeSaveReader(stats: null);
        var (vm, levels) = BuildVm(factory, reader);
        await vm.LoadAsync();

        vm.NewLevelName = "The Nightmare";
        vm.NewLevelGdId = "не число";
        await vm.AddLevelCommand.ExecuteAsync(null);

        vm.Error.Should().NotBeNullOrEmpty();
        var all = await levels.GetAllAsync();
        all.Should().BeEmpty();
    }

    [Fact]
    public async Task Manual_add_with_gd_id_already_in_database_reports_duplicate_and_skips_creation()
    {
        using var factory = new InMemorySqlite();
        var reader = new FakeSaveReader(stats: null);
        var (vm, levels) = BuildVm(factory, reader);
        await levels.AddAsync(new Level { Name = "Уже есть", GdLevelId = 13519 });
        await vm.LoadAsync();

        vm.NewLevelName = "The Nightmare";
        vm.NewLevelGdId = "13519";
        await vm.AddLevelCommand.ExecuteAsync(null);

        vm.Error.Should().NotBeNullOrEmpty();
        var all = await levels.GetAllAsync();
        all.Should().ContainSingle();
    }

    [Fact]
    public async Task Manual_add_with_gd_id_then_import_from_game_does_not_duplicate_level()
    {
        using var factory = new InMemorySqlite();
        var reader = new FakeSaveReader(stats: null) { Levels = [SaveDto(13519, 72, 158)] };
        var (vm, levels) = BuildVm(factory, reader);
        await vm.LoadAsync();

        vm.NewLevelName = "The Nightmare";
        vm.NewLevelGdId = "13519";
        await vm.AddLevelCommand.ExecuteAsync(null);

        // Симулируем последующий «Импорт из игры» — то же самое, что делает
        // LevelsViewModel.ImportFromGameAsync внутри (чтение сейва + SaveImportService).
        var importer = new SaveImportService(factory);
        await importer.ImportAsync(reader.Levels);

        var all = await levels.GetAllAsync();
        all.Should().ContainSingle("до фикса импорт заводил вторую строку без GdLevelId");
        var level = all[0];
        level.GdLevelId.Should().Be(13519);
        level.BestNormalPercent.Should().Be(72);
        level.TotalAttempts.Should().Be(158);
    }
}
