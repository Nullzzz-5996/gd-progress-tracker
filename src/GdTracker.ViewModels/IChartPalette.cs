using SkiaSharp;

namespace GdTracker.ViewModels;

/// <summary>
/// Палитра цветов графиков статистики, зависящая от текущей темы оформления
/// (реализуется в слое UI). В отличие от <see cref="IThemeService"/> ей нельзя
/// зависеть от WPF: тип цвета — <see cref="SKColor"/> из SkiaSharp, на который
/// слой view-моделей и так уже завязан через LiveCharts.
/// </summary>
public interface IChartPalette
{
    /// <summary>Цвет линии графика количества звёзд.</summary>
    SKColor StarsLineColor { get; }

    /// <summary>Цвет линии графика количества демонов.</summary>
    SKColor DemonsLineColor { get; }

    /// <summary>Цвет подписей осей (числа и даты рядом с осью).</summary>
    SKColor AxisLabelColor { get; }

    /// <summary>Цвет линий осей (засечки и разделители).</summary>
    SKColor AxisLineColor { get; }

    /// <summary>
    /// Срабатывает, когда цвета палитры изменились (при смене темы оформления
    /// через <see cref="IThemeService"/>) — открытая страница статистики должна
    /// перестроить серии графика немедленно, а не ждать перехода на другую
    /// вкладку и обратно.
    /// </summary>
    event EventHandler? Changed;
}
