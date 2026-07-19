using GdTracker.Core;

namespace GdTracker.ViewModels;

/// <summary>Абстракция применения темы оформления приложения (реализуется в слое UI).</summary>
public interface IThemeService
{
    /// <summary>Применяет тему: штатную тему WPF-UI и ресурсы оформления страниц.</summary>
    void ApplyTheme(AppTheme theme);
}
