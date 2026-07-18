using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using GdTracker.Core.Abstractions;

namespace GdTracker.ViewModels;

/// <summary>View-модель страницы настроек: версия приложения и путь к сейв-файлу GD.</summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly ISaveFileReader _saveReader;

    public SettingsViewModel(ISettingsService settings, ISaveFileReader saveReader)
    {
        _settings = settings;
        _saveReader = saveReader;
        _saveFilePath = settings.SaveFilePath ?? saveReader.DefaultSaveFilePath ?? string.Empty;
    }

    [ObservableProperty]
    private string _appVersion =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";

    [ObservableProperty] private string _saveFilePath;

    partial void OnSaveFilePathChanged(string value)
        => _settings.SetSaveFilePath(string.IsNullOrWhiteSpace(value) ? null : value);
}
