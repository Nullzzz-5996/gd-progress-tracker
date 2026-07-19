using FluentAssertions;
using GdTracker.Core;
using GdTracker.Data;

namespace GdTracker.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gdtracker-tests", Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_dir, "settings.json");

    [Fact]
    public void Missing_file_yields_null_path_and_does_not_throw()
    {
        var settings = new SettingsService(SettingsPath);

        settings.SaveFilePath.Should().BeNull();
    }

    [Fact]
    public void Path_survives_a_new_instance()
    {
        new SettingsService(SettingsPath).SetSaveFilePath(@"C:\games\CCGameManager.dat");

        new SettingsService(SettingsPath).SaveFilePath.Should().Be(@"C:\games\CCGameManager.dat");
    }

    [Fact]
    public void Path_can_be_cleared()
    {
        var settings = new SettingsService(SettingsPath);
        settings.SetSaveFilePath(@"C:\games\CCGameManager.dat");

        settings.SetSaveFilePath(null);

        new SettingsService(SettingsPath).SaveFilePath.Should().BeNull();
    }

    [Fact]
    public void Corrupt_file_falls_back_to_defaults_instead_of_throwing()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, "{ это не json");

        var settings = new SettingsService(SettingsPath);

        settings.SaveFilePath.Should().BeNull();
    }

    [Fact]
    public void Save_does_not_leave_temp_file()
    {
        Directory.CreateDirectory(_dir);
        var settings = new SettingsService(SettingsPath);

        settings.SetSaveFilePath(@"C:\games\CCGameManager.dat");

        var tempPath = SettingsPath + ".tmp";
        File.Exists(tempPath).Should().BeFalse("временный файл должен быть удалён после успешного сохранения");

        var filesInDir = Directory.GetFiles(_dir);
        filesInDir.Should().HaveCount(1).And.Contain(SettingsPath);
    }

    [Fact]
    public void Missing_file_yields_dark_theme_by_default()
    {
        var settings = new SettingsService(SettingsPath);

        settings.Theme.Should().Be(AppTheme.Dark);
    }

    [Fact]
    public void Theme_survives_a_new_instance()
    {
        new SettingsService(SettingsPath).SetTheme(AppTheme.Neon);

        new SettingsService(SettingsPath).Theme.Should().Be(AppTheme.Neon);
    }

    [Fact]
    public void Garbage_theme_value_falls_back_to_dark_instead_of_throwing()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, """{ "Theme": "Кислотная" }""");

        var settings = new SettingsService(SettingsPath);

        settings.Theme.Should().Be(AppTheme.Dark);
    }

    [Fact]
    public void Numeric_garbage_theme_value_falls_back_to_dark_instead_of_throwing()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, """{ "Theme": 999 }""");

        var settings = new SettingsService(SettingsPath);

        settings.Theme.Should().Be(AppTheme.Dark);
    }

    [Fact]
    public void Setting_theme_does_not_lose_previously_saved_save_path()
    {
        var settings = new SettingsService(SettingsPath);
        settings.SetSaveFilePath(@"C:\games\CCGameManager.dat");

        settings.SetTheme(AppTheme.Light);

        var reloaded = new SettingsService(SettingsPath);
        reloaded.SaveFilePath.Should().Be(@"C:\games\CCGameManager.dat");
        reloaded.Theme.Should().Be(AppTheme.Light);
    }

    [Fact]
    public void Setting_save_path_does_not_lose_previously_saved_theme()
    {
        var settings = new SettingsService(SettingsPath);
        settings.SetTheme(AppTheme.Neon);

        settings.SetSaveFilePath(@"C:\games\CCGameManager.dat");

        var reloaded = new SettingsService(SettingsPath);
        reloaded.Theme.Should().Be(AppTheme.Neon);
        reloaded.SaveFilePath.Should().Be(@"C:\games\CCGameManager.dat");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
