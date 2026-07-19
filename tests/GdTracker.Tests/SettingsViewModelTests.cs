using FluentAssertions;
using GdTracker.Core;
using GdTracker.Core.Abstractions;
using GdTracker.Core.Models;
using GdTracker.ViewModels;

namespace GdTracker.Tests;

/// <summary>
/// Тесты для SettingsViewModel: проверка того, что конструирование не вызывает
/// SetSaveFilePath/ApplyTheme/SetTheme, но изменение свойств вызывает.
/// </summary>
public class SettingsViewModelTests
{
    [Fact]
    public void Settings_vm_construction_with_saved_path_does_not_call_set_save_file_path()
    {
        var settings = new FakeSettings();
        settings.SetSaveFilePath(@"C:\custom\CCGameManager.dat");
        settings.SetSaveFilePathCallCount = 0; // сбрасываем счётчик

        var vm = new SettingsViewModel(settings, new FakeSaveFileReader(), new FakeThemeService());

        // При конструировании не должно быть вызовов SetSaveFilePath.
        settings.SetSaveFilePathCallCount.Should().Be(0);
        vm.SaveFilePath.Should().Be(@"C:\custom\CCGameManager.dat");
    }

    [Fact]
    public void Settings_vm_property_change_calls_set_save_file_path()
    {
        var settings = new FakeSettings();
        settings.SetSaveFilePath(@"C:\initial\CCGameManager.dat");
        settings.SetSaveFilePathCallCount = 0;

        var vm = new SettingsViewModel(settings, new FakeSaveFileReader(), new FakeThemeService());

        // Изменение свойства после конструирования должно вызвать SetSaveFilePath.
        vm.SaveFilePath = @"C:\new\CCGameManager.dat";

        settings.SetSaveFilePathCallCount.Should().Be(1);
        settings.SaveFilePath.Should().Be(@"C:\new\CCGameManager.dat");
    }

    [Fact]
    public void Settings_vm_construction_does_not_apply_theme_or_save_it()
    {
        var settings = new FakeSettings();
        settings.SetTheme(AppTheme.Neon);
        settings.SetThemeCallCount = 0; // сбрасываем счётчик
        var themeService = new FakeThemeService();

        var vm = new SettingsViewModel(settings, new FakeSaveFileReader(), themeService);

        // При конструировании не должно быть ни применения темы, ни записи в настройки.
        themeService.ApplyThemeCallCount.Should().Be(0);
        settings.SetThemeCallCount.Should().Be(0);
        vm.SelectedTheme.Should().Be(AppTheme.Neon);
    }

    [Fact]
    public void Settings_vm_selected_theme_change_applies_and_saves_theme_exactly_once()
    {
        var settings = new FakeSettings();
        settings.SetTheme(AppTheme.Dark);
        settings.SetThemeCallCount = 0;
        var themeService = new FakeThemeService();

        var vm = new SettingsViewModel(settings, new FakeSaveFileReader(), themeService);

        vm.SelectedTheme = AppTheme.Light;

        themeService.ApplyThemeCallCount.Should().Be(1);
        themeService.LastAppliedTheme.Should().Be(AppTheme.Light);
        settings.SetThemeCallCount.Should().Be(1);
        settings.Theme.Should().Be(AppTheme.Light);
    }

    [Fact]
    public void Settings_vm_selected_theme_is_initialized_from_settings()
    {
        var settings = new FakeSettings();
        settings.SetTheme(AppTheme.Light);

        var vm = new SettingsViewModel(settings, new FakeSaveFileReader(), new FakeThemeService());

        // Выбранная тема при создании должна браться из настроек, а не быть всегда тёмной.
        vm.SelectedTheme.Should().Be(AppTheme.Light);
    }

    /// <summary>Фейковый ридер сейва для упрощённых тестов SettingsViewModel.</summary>
    private sealed class FakeSaveFileReader : ISaveFileReader
    {
        public string? DefaultSaveFilePath { get; set; } = @"C:\fake\CCGameManager.dat";

        public IReadOnlyList<SaveLevelDto> ReadLevels(string saveFilePath) => new List<SaveLevelDto>();
        public AccountStats? ReadAccountStats(string saveFilePath) => null;
        public DateTime? GetLastWriteTimeUtc(string saveFilePath) => null;
    }

    /// <summary>Фейковый сервис применения темы, считающий вызовы.</summary>
    private sealed class FakeThemeService : IThemeService
    {
        public int ApplyThemeCallCount { get; private set; }
        public AppTheme? LastAppliedTheme { get; private set; }

        public void ApplyTheme(AppTheme theme)
        {
            ApplyThemeCallCount++;
            LastAppliedTheme = theme;
        }
    }
}
