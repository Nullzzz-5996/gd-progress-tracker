using FluentAssertions;
using GdTracker.Core;
using GdTracker.Core.Models;
using GdTracker.Data;
using GdTracker.Data.Repositories;

namespace GdTracker.Tests;

public class SaveProgressLookupServiceTests
{
    private static SaveLevelDto Dto(long id, int normal, int attempts) => new()
    {
        GdLevelId = id,
        Name = "Из сейва",
        Source = LevelSource.Online,
        BestNormalPercent = normal,
        Attempts = attempts,
    };

    [Fact]
    public async Task Level_found_in_save_applies_progress_to_existing_row()
    {
        using var factory = new InMemorySqlite();
        var levels = new LevelRepository(factory);
        var importer = new SaveImportService(factory);
        var reader = new FakeSaveReader(stats: null) { Levels = [Dto(13519, 72, 158)] };
        var sut = new SaveProgressLookupService(new FakeSettings(), reader, importer);

        var level = await levels.AddAsync(new Level
        {
            GdLevelId = 13519, Name = "Test Level", Source = LevelSource.Online,
        });

        var found = await sut.TryApplyProgressAsync(13519);

        found.Should().BeTrue();
        var reloaded = await levels.GetByIdAsync(level.Id);
        reloaded!.BestNormalPercent.Should().Be(72);
        reloaded.TotalAttempts.Should().Be(158);
    }

    [Fact]
    public async Task Level_absent_from_save_returns_false_and_leaves_row_untouched()
    {
        using var factory = new InMemorySqlite();
        var levels = new LevelRepository(factory);
        var importer = new SaveImportService(factory);
        // Сейв содержит другие уровни, но не искомый.
        var reader = new FakeSaveReader(stats: null) { Levels = [Dto(999, 100, 10)] };
        var sut = new SaveProgressLookupService(new FakeSettings(), reader, importer);

        var level = await levels.AddAsync(new Level
        {
            GdLevelId = 13519, Name = "Test Level", Source = LevelSource.Online,
        });

        var found = await sut.TryApplyProgressAsync(13519);

        found.Should().BeFalse();
        var reloaded = await levels.GetByIdAsync(level.Id);
        reloaded!.TotalAttempts.Should().Be(0);
        reloaded.BestNormalPercent.Should().Be(0);
    }

    [Fact]
    public async Task Save_path_unavailable_returns_false_without_reading_or_throwing()
    {
        using var factory = new InMemorySqlite();
        var importer = new SaveImportService(factory);
        // Ни настроенного пути, ни автоопределённого — как при первом запуске без сейва.
        var reader = new FakeSaveReader(stats: null) { DefaultSaveFilePath = null };
        var sut = new SaveProgressLookupService(new FakeSettings(), reader, importer);

        var found = await sut.TryApplyProgressAsync(13519);

        found.Should().BeFalse();
        reader.ReadLevelsCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Corrupt_or_locked_save_file_returns_false_without_throwing()
    {
        using var factory = new InMemorySqlite();
        var importer = new SaveImportService(factory);
        var reader = new FakeSaveReader(stats: null) { ThrowOnReadLevels = true };
        var sut = new SaveProgressLookupService(new FakeSettings(), reader, importer);

        var found = await sut.TryApplyProgressAsync(13519);

        found.Should().BeFalse();
    }

    [Fact]
    public async Task Settings_path_wins_over_default_autodetected_path()
    {
        using var factory = new InMemorySqlite();
        var importer = new SaveImportService(factory);
        var reader = new FakeSaveReader(stats: null)
        {
            Levels = [Dto(13519, 55, 20)],
            DefaultSaveFilePath = @"C:\autodetected\CCGameManager.dat",
        };
        var settings = new FakeSettings();
        settings.SetSaveFilePath(@"D:\custom\CCGameManager.dat");
        var sut = new SaveProgressLookupService(settings, reader, importer);

        var found = await sut.TryApplyProgressAsync(13519);

        found.Should().BeTrue();
        reader.LastReadLevelsPath.Should().Be(@"D:\custom\CCGameManager.dat");
    }
}
