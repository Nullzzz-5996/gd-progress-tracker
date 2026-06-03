using FluentAssertions;
using GdTracker.Core;
using GdTracker.Core.Models;
using GdTracker.Data;
using GdTracker.Data.Repositories;
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

            var levelsVm = new LevelsViewModel(levels, progress);
            await levelsVm.LoadAsync();

            levelsVm.NewLevelName = "Bloodbath";
            levelsVm.NewLevelSource = LevelSource.Online;
            await levelsVm.AddLevelCommand.ExecuteAsync(null);

            levelsVm.Levels.Should().ContainSingle();
            levelsVm.SelectedLevel.Should().NotBeNull();
            levelId = levelsVm.SelectedLevel!.Id;

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

    public void Dispose()
    {
        foreach (var f in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            try { if (File.Exists(f)) File.Delete(f); }
            catch { /* игнорируем — временный файл */ }
        }
    }
}
