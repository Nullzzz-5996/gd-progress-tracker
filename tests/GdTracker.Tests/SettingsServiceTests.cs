using FluentAssertions;
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

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
