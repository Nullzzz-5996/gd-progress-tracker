using FluentAssertions;
using GdTracker.Core.Abstractions;
using GdTracker.Core.Models;
using GdTracker.ViewModels;

namespace GdTracker.Tests;

/// <summary>
/// Тесты для SettingsViewModel: проверка того, что конструирование не вызывает
/// SetSaveFilePath, но изменение свойства вызывает.
/// </summary>
public class SettingsViewModelTests
{
    [Fact]
    public void Settings_vm_construction_with_saved_path_does_not_call_set_save_file_path()
    {
        var settings = new FakeSettings();
        settings.SetSaveFilePath(@"C:\custom\CCGameManager.dat");
        settings.SetSaveFilePathCallCount = 0; // сбрасываем счётчик

        var vm = new SettingsViewModel(settings, new FakeSaveFileReader());

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

        var vm = new SettingsViewModel(settings, new FakeSaveFileReader());

        // Изменение свойства после конструирования должно вызвать SetSaveFilePath.
        vm.SaveFilePath = @"C:\new\CCGameManager.dat";

        settings.SetSaveFilePathCallCount.Should().Be(1);
        settings.SaveFilePath.Should().Be(@"C:\new\CCGameManager.dat");
    }

    /// <summary>Фейковый ридер сейва для упрощённых тестов SettingsViewModel.</summary>
    private sealed class FakeSaveFileReader : ISaveFileReader
    {
        public string? DefaultSaveFilePath { get; set; } = @"C:\fake\CCGameManager.dat";

        public IReadOnlyList<SaveLevelDto> ReadLevels(string saveFilePath) => new List<SaveLevelDto>();
        public AccountStats? ReadAccountStats(string saveFilePath) => null;
        public DateTime? GetLastWriteTimeUtc(string saveFilePath) => null;
    }
}
