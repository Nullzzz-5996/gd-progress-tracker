using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GdTracker.App.Converters;

/// <summary>
/// Ширина колонки с деталью выбранного уровня: ноль, пока деталь не выбрана.
/// Без этого пустая панель занимала бы больше половины ширины окна на старте,
/// а список уровней ужимался бы до 41% и обрезал колонки.
/// </summary>
public sealed class DetailPaneWidthConverter : IValueConverter
{
    /// <summary>Доля ширины, отдаваемая детали, когда уровень выбран.</summary>
    public double SelectedStarWidth { get; set; } = 2;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null
            ? new GridLength(0)
            : new GridLength(SelectedStarWidth, GridUnitType.Star);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Показывает элемент только когда значение не null (разделитель между списком и деталью).</summary>
public sealed class NotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
