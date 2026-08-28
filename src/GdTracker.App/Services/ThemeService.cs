using System;
using System.Windows;
using System.Windows.Media;
using GdTracker.Core;
using GdTracker.ViewModels;
using Wpf.Ui.Appearance;

namespace GdTracker.App.Services;

/// <inheritdoc />
public class ThemeService : IThemeService
{
    // Ссылка на ранее подмешанный словарь темы: без неё при повторном
    // применении темы старый словарь накапливался бы в MergedDictionaries
    // вместе с новым, а не заменялся им.
    private ResourceDictionary? _currentThemeDictionary;

    /// <summary>Акцент неоновой темы — фуксия, тот же цвет, что у звёзд и линии графика.</summary>
    private static readonly Color NeonAccent = Color.FromRgb(0xFF, 0x61, 0xD8);

    private readonly ChartPaletteService _chartPalette;

    public ThemeService(ChartPaletteService chartPalette)
    {
        _chartPalette = chartPalette;
    }

    public void ApplyTheme(AppTheme theme)
    {
        // Палитра графиков статистики зависит от той же темы: без этого вызова открытая
        // страница статистики не узнала бы о смене темы и не перестроила бы цвета серий.
        _chartPalette.ApplyTheme(theme);

        // Штатная тема WPF-UI знает только светлую и тёмную: неоновая тема
        // использует тёмную базу (окна, стандартные элементы управления),
        // а фиолетовый колорит даёт наш собственный словарь ресурсов.
        var applicationTheme = theme == AppTheme.Light ? ApplicationTheme.Light : ApplicationTheme.Dark;
        ApplicationThemeManager.Apply(applicationTheme);

        // Акцент WPF-UI (индикатор выбранного пункта навигации, фокус, акцентные кнопки,
        // индикаторы прогресса) живёт отдельно от словаря темы и сбрасывается вызовом
        // ApplicationThemeManager.Apply, поэтому задаётся сразу после него: неоновой теме —
        // фуксия, тёмной и светлой — системный акцент, как и было.
        if (theme == AppTheme.Neon)
            ApplicationAccentColorManager.Apply(NeonAccent, applicationTheme);
        else
            ApplicationAccentColorManager.ApplySystemAccent();

        var dictionaries = Application.Current.Resources.MergedDictionaries;

        if (_currentThemeDictionary is not null)
        {
            dictionaries.Remove(_currentThemeDictionary);
        }

        var themeDictionary = new ResourceDictionary
        {
            Source = new Uri($"/GdTracker.App;component/Themes/{theme}.xaml", UriKind.Relative),
        };
        dictionaries.Add(themeDictionary);
        _currentThemeDictionary = themeDictionary;
    }
}
