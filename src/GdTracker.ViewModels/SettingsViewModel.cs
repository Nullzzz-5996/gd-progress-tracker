using System.Collections.Generic;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using GdTracker.Core;
using GdTracker.Core.Abstractions;

namespace GdTracker.ViewModels;

/// <summary>Пара «значение перечисления темы — русская подпись» для выпадающего списка.</summary>
public sealed record ThemeOption(AppTheme Value, string Label);

/// <summary>
/// View-модель страницы настроек: версия приложения, путь к сейв-файлу GD
/// и тема оформления.
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly ISaveFileReader _saveReader;
    private readonly IThemeService _themeService;

    public SettingsViewModel(ISettingsService settings, ISaveFileReader saveReader, IThemeService themeService)
    {
        _settings = settings;
        _saveReader = saveReader;
        _themeService = themeService;
        _saveFilePath = settings.SaveFilePath ?? saveReader.DefaultSaveFilePath ?? string.Empty;
        // Присваиваем полю напрямую, а не через свойство: иначе сгенерированный
        // OnSelectedThemeChanged сработал бы уже при создании вью-модели и
        // применил/пересохранил тему при каждом открытии страницы настроек.
        _selectedTheme = settings.Theme;
    }

    [ObservableProperty]
    private string _appVersion =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";

    [ObservableProperty] private string _saveFilePath;

    partial void OnSaveFilePathChanged(string value)
        => _settings.SetSaveFilePath(string.IsNullOrWhiteSpace(value) ? null : value);

    /// <summary>Доступные темы оформления с русскими подписями для выпадающего списка.</summary>
    public IReadOnlyList<ThemeOption> AvailableThemes { get; } =
    [
        new ThemeOption(AppTheme.Dark, "Тёмная"),
        new ThemeOption(AppTheme.Light, "Светлая"),
        new ThemeOption(AppTheme.Neon, "Неоновая"),
    ];

    [ObservableProperty] private AppTheme _selectedTheme;

    partial void OnSelectedThemeChanged(AppTheme value)
    {
        _themeService.ApplyTheme(value);
        _settings.SetTheme(value);
    }
}
