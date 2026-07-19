using GdTracker.Core;
using GdTracker.Core.Abstractions;
using GdTracker.ViewModels;
using SkiaSharp;

namespace GdTracker.App.Services;

/// <inheritdoc cref="IChartPalette" />
/// <remarks>
/// Цвета звёзд и демонов для тёмной и светлой тем совпадают с акцентами карточек
/// сводки аккаунта (см. StarsAccentBrush/DemonsAccentBrush в Themes/Dark.xaml и
/// Themes/Light.xaml) — так серия графика и карточка над ним говорят одним цветом.
/// Неоновая тема разводит звёзды и демоны по розовому и голубому (синтвейв), чтобы
/// обе линии оставались различимы на тёмно-фиолетовом фоне.
/// </remarks>
public sealed class ChartPaletteService : IChartPalette
{
    private AppTheme _theme;

    public ChartPaletteService(ISettingsService settings)
    {
        _theme = settings.Theme;
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public SKColor StarsLineColor => _theme switch
    {
        AppTheme.Light => new SKColor(0xC9, 0x97, 0x1C),
        AppTheme.Neon => new SKColor(0xFF, 0x61, 0xD8),
        _ => new SKColor(0xFF, 0xD5, 0x4F),
    };

    /// <inheritdoc />
    public SKColor DemonsLineColor => _theme switch
    {
        AppTheme.Light => new SKColor(0xC6, 0x28, 0x28),
        AppTheme.Neon => new SKColor(0x01, 0xCD, 0xFE),
        _ => new SKColor(0xEF, 0x53, 0x50),
    };

    /// <inheritdoc />
    public SKColor AxisLabelColor => _theme switch
    {
        AppTheme.Light => new SKColor(0x21, 0x21, 0x21),
        AppTheme.Neon => new SKColor(0xEA, 0xD9, 0xFF),
        _ => new SKColor(0xE0, 0xE0, 0xE0),
    };

    /// <inheritdoc />
    public SKColor AxisLineColor => _theme switch
    {
        AppTheme.Light => new SKColor(0x4D, 0x4D, 0x4D),
        AppTheme.Neon => new SKColor(0x9D, 0x7B, 0xC7),
        _ => new SKColor(0x80, 0x80, 0x80),
    };

    /// <summary>
    /// Вызывается <see cref="ThemeService"/> при смене темы: запоминает новую тему
    /// и уведомляет подписчиков (открытую страницу статистики), чтобы график
    /// перестроился сразу, не дожидаясь перехода на другую вкладку и обратно.
    /// </summary>
    public void ApplyTheme(AppTheme theme)
    {
        if (_theme == theme)
            return;

        _theme = theme;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
